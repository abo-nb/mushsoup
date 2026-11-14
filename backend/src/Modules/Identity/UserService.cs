using System.Security.Claims;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Classes.Contracts;
using Identity.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Identity;

internal sealed class UserDocument
{
    /// <summary>Discord 用户 ID，直接作为 _id。</summary>
    [BsonId]
    public string Id { get; set; } = "";

    [BsonElement("username")]
    public string Username { get; set; } = "";

    [BsonElement("globalName")]
    public string? GlobalName { get; set; }

    [BsonElement("nickname")]
    public string? Nickname { get; set; }

    [BsonElement("avatarUrl")]
    public string? AvatarUrl { get; set; }

    [BsonElement("displayName")]
    public string DisplayName { get; set; } = "";

    [BsonElement("classId")]
    public string? ClassId { get; set; }

    [BsonElement("prefs")]
    public UserPrefsDocument Prefs { get; set; } = new();

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }
}

internal sealed class UserPrefsDocument
{
    [BsonElement("shuffleOptions")]
    public string ShuffleOptions { get; set; } = UserPrefsDto.Default.ShuffleOptions;

    [BsonElement("submitMode")]
    public string SubmitMode { get; set; } = UserPrefsDto.Default.SubmitMode;
}

public sealed class UserService(
    MongoDatabaseProvider provider,
    VerificationTicketStore tickets,
    DiscordVerificationClient verification,
    IClassDirectory classes,
    ICurrentUser currentUser,
    IHttpContextAccessor httpContextAccessor)
{
    private IMongoCollection<UserDocument> Users => provider.Collection<UserDocument>("users");

    public static async Task EnsureIndexesAsync(MongoDatabaseProvider provider, CancellationToken cancellationToken)
    {
        var users = provider.Collection<UserDocument>("users");
        await users.Indexes.CreateOneAsync(
            new CreateIndexModel<UserDocument>(
                Builders<UserDocument>.IndexKeys.Ascending(user => user.ClassId)),
            cancellationToken: cancellationToken);

        // 旧的邮箱唯一索引在缺 email 字段的文档上会互相冲突，登录切换后不再需要。
        try
        {
            await users.Indexes.DropOneAsync("email_1", cancellationToken);
        }
        catch (MongoCommandException ex) when (ex.Code == 27)
        {
        }
    }

    public async Task<VerifyMembersDto> GetMembersAsync(bool refresh, CancellationToken cancellationToken) =>
        await verification.GetMembersAsync(refresh, cancellationToken);

    public async Task<VerifyTicketDto> RequestVerificationAsync(
        VerifyRequest request,
        CancellationToken cancellationToken)
    {
        var discordUserId = request.UserId?.Trim() ?? "";
        if (discordUserId.Length == 0)
        {
            throw new AppException("invalid_request", "请填写 Discord 用户 ID", StatusCodes.Status400BadRequest);
        }

        var start = await verification.RequestAsync(discordUserId, cancellationToken);
        var ticket = new VerificationTicketDocument
        {
            Id = Ids.New(),
            RequestId = start.RequestId,
            DiscordUserId = discordUserId,
            Delivery = start.Delivery,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = start.ExpiresAt,
        };

        await tickets.CreateAsync(ticket, cancellationToken);
        return new VerifyTicketDto(ticket.Id, ticket.Delivery, ticket.ExpiresAt);
    }

    public async Task<UserDto> ConfirmVerificationAsync(
        VerifyConfirmRequest request,
        CancellationToken cancellationToken)
    {
        var ticketId = request.TicketId?.Trim() ?? "";
        var code = request.Code?.Trim() ?? "";

        var ticket = ticketId.Length == 0 ? null : await tickets.GetAsync(ticketId, cancellationToken);
        if (ticket is null || ticket.ExpiresAt <= DateTime.UtcNow)
        {
            throw new AppException(
                "ticket_expired", "验证已过期，请重新获取验证码", StatusCodes.Status400BadRequest);
        }

        if (code.Length == 0)
        {
            throw new AppException(
                "invalid_code", "验证码不正确或已过期", StatusCodes.Status400BadRequest);
        }

        var accessToken = await verification.ConfirmAsync(ticket.RequestId, code, cancellationToken);
        var identity = await verification.GetMeAsync(accessToken, cancellationToken);

        var user = await SaveVerifiedUserAsync(identity, cancellationToken);
        await SignInAsync(user);
        await tickets.DeleteAsync(ticket.Id, cancellationToken);

        return ToDto(user);
    }

    public async Task LogoutAsync()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new AppException("unauthorized", "未登录", StatusCodes.Status401Unauthorized);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<UserDto> GetMeAsync(CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<UserDto> UpdatePrefsAsync(UpdatePrefsRequest request, CancellationToken cancellationToken)
    {
        var update = Builders<UserDocument>.Update;
        var changes = new List<UpdateDefinition<UserDocument>>();

        if (request.ShuffleOptions is not null)
        {
            if (request.ShuffleOptions is not ("follow" or "always" or "never"))
            {
                throw new ValidationFailedException("shuffleOptions", "shuffleOptions 只能是 follow、always 或 never");
            }

            changes.Add(update.Set(candidate => candidate.Prefs.ShuffleOptions, request.ShuffleOptions));
        }

        if (request.SubmitMode is not null)
        {
            if (request.SubmitMode is not ("space" or "auto"))
            {
                throw new ValidationFailedException("submitMode", "submitMode 只能是 space 或 auto");
            }

            changes.Add(update.Set(candidate => candidate.Prefs.SubmitMode, request.SubmitMode));
        }

        if (changes.Count == 0)
        {
            throw new ValidationFailedException("prefs", "没有要更新的设置");
        }

        var userId = currentUser.RequireUserId();
        var user = await Users.FindOneAndUpdateAsync(
            candidate => candidate.Id == userId && candidate.DeletedAt == null,
            update.Combine(changes),
            new FindOneAndUpdateOptions<UserDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken)
            ?? throw new NotFoundException("用户不存在");

        return ToDto(user);
    }

    private async Task<UserDocument> SaveVerifiedUserAsync(
        VerificationIdentity identity,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var displayName = identity.Nickname ?? identity.GlobalName ?? identity.Username;

        var existing = await Users
            .Find(candidate => candidate.Id == identity.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            // 默认班级可能还没被任何人创建，GetDefaultAsync 会兜底建一个。
            var defaultClass = await classes.GetDefaultAsync(cancellationToken);
            var document = new UserDocument
            {
                Id = identity.Id,
                Username = identity.Username,
                GlobalName = identity.GlobalName,
                Nickname = identity.Nickname,
                AvatarUrl = identity.AvatarUrl,
                DisplayName = displayName,
                ClassId = defaultClass.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };

            try
            {
                await Users.InsertOneAsync(document, cancellationToken: cancellationToken);
                return document;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                existing = await Users
                    .Find(candidate => candidate.Id == identity.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing is null)
                {
                    throw;
                }
            }
        }

        var classId = string.IsNullOrWhiteSpace(existing.ClassId)
            ? (await classes.GetDefaultAsync(cancellationToken)).Id
            : existing.ClassId;

        return await Users.FindOneAndUpdateAsync(
            candidate => candidate.Id == existing.Id,
            Builders<UserDocument>.Update
                .Set(candidate => candidate.Username, identity.Username)
                .Set(candidate => candidate.GlobalName, identity.GlobalName)
                .Set(candidate => candidate.Nickname, identity.Nickname)
                .Set(candidate => candidate.AvatarUrl, identity.AvatarUrl)
                .Set(candidate => candidate.DisplayName, displayName)
                .Set(candidate => candidate.ClassId, classId)
                .Set(candidate => candidate.UpdatedAt, now),
            new FindOneAndUpdateOptions<UserDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken)
            ?? existing;
    }

    private async Task<UserDocument> RequireUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        return await Users
            .Find(candidate => candidate.Id == userId && candidate.DeletedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("用户不存在");
    }

    private async Task SignInAsync(UserDocument user)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new AppException("unauthorized", "未登录", StatusCodes.Status401Unauthorized);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("class_id", user.ClassId ?? ""),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
            });
    }

    private static UserDto ToDto(UserDocument user) => new(
        user.Id,
        user.Username,
        user.DisplayName,
        user.AvatarUrl,
        user.ClassId,
        new UserPrefsDto(
            user.Prefs?.ShuffleOptions ?? UserPrefsDto.Default.ShuffleOptions,
            user.Prefs?.SubmitMode ?? UserPrefsDto.Default.SubmitMode));
}
