using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Contracts;
using Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity;

/// <summary>上游确认成功后的会话凭据，用完即弃，不落库也不回传前端。</summary>
public sealed record VerificationStart(string RequestId, string Delivery, DateTime ExpiresAt);

/// <summary>上游 /api/verify/me 返回的本人资料。</summary>
public sealed record VerificationIdentity(
    string Id,
    string Username,
    string? GlobalName,
    string? Nickname,
    string? AvatarUrl);

/// <summary>外部 Discord 身份验证服务的 typed client。</summary>
public sealed class DiscordVerificationClient(
    HttpClient httpClient,
    IOptions<VerificationOptions> options,
    IMemoryCache cache,
    ILogger<DiscordVerificationClient> logger)
{
    private const string MembersCacheKey = "identity:verify-members";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // 成员列表可能很慢（上游要分页拉人再逐个算频道权限），缓存一小段时间；
    // 刷新按钮带 refresh=true 绕过缓存。用信号量避免并发刷新打出多次上游请求。
    private static readonly TimeSpan MembersCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim MembersRefreshLock = new(1, 1);

    private VerificationOptions Options => options.Value;

    public async Task<VerifyMembersDto> GetMembersAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (!refresh && TryGetCachedMembers(out var cached))
        {
            return cached;
        }

        await MembersRefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && TryGetCachedMembers(out cached))
            {
                return cached;
            }

            var members = await FetchMembersAsync(cancellationToken);
            cache.Set(MembersCacheKey, members, MembersCacheDuration);
            return members;
        }
        finally
        {
            MembersRefreshLock.Release();
        }
    }

    private bool TryGetCachedMembers(out VerifyMembersDto members)
    {
        if (cache.TryGetValue(MembersCacheKey, out VerifyMembersDto? cached) && cached is not null)
        {
            members = cached;
            return true;
        }

        members = null!;
        return false;
    }

    private async Task<VerifyMembersDto> FetchMembersAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var response = await SendAsync(
            HttpMethod.Get,
            "api/verify/members",
            content: null,
            accessToken: null,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw MapFailure(response.StatusCode, membersEndpoint: true);
        }

        var payload = await response.Content.ReadFromJsonAsync<MembersPayload>(JsonOptions, cancellationToken)
            ?? throw Unavailable();

        var members = payload.Members?
            .Where(member => !string.IsNullOrWhiteSpace(member.Id))
            .Select(member => new VerifyMemberDto(
                member.Id!,
                member.Username ?? "",
                member.GlobalName,
                member.Nickname,
                member.Nickname ?? member.GlobalName ?? member.Username ?? "",
                member.AvatarUrl))
            .ToList() ?? [];

        return new VerifyMembersDto(payload.Count ?? members.Count, members);
    }

    public async Task<VerificationStart> RequestAsync(string userId, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var response = await SendAsync(
            HttpMethod.Post,
            "api/verify/request",
            JsonContent.Create(new RequestPayload(userId), options: JsonOptions),
            accessToken: null,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw MapFailure(response.StatusCode, membersEndpoint: false);
        }

        var payload = await response.Content.ReadFromJsonAsync<RequestPayloadResponse>(JsonOptions, cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.RequestId))
        {
            throw Unavailable();
        }

        var expiresAt = ToUtc(payload.ExpiresAt) ?? DateTime.UtcNow.AddMinutes(10);
        if (expiresAt <= DateTime.UtcNow)
        {
            expiresAt = DateTime.UtcNow.AddMinutes(10);
        }

        var delivery = string.Equals(payload.Delivery, "thread", StringComparison.OrdinalIgnoreCase)
            ? "thread"
            : "dm";

        return new VerificationStart(payload.RequestId!, delivery, expiresAt);
    }

    public async Task<string> ConfirmAsync(string requestId, string code, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var response = await SendAsync(
            HttpMethod.Post,
            "api/verify/confirm",
            JsonContent.Create(new ConfirmPayload(requestId, code), options: JsonOptions),
            accessToken: null,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new AppException("invalid_code", "验证码不正确或已过期", StatusCodes.Status400BadRequest);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw MapFailure(response.StatusCode, membersEndpoint: false);
        }

        var payload = await response.Content.ReadFromJsonAsync<ConfirmPayloadResponse>(JsonOptions, cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            throw Unavailable();
        }

        return payload.AccessToken!;
    }

    public async Task<VerificationIdentity> GetMeAsync(string accessToken, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var response = await SendAsync(
            HttpMethod.Get,
            "api/verify/me",
            content: null,
            accessToken,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw MapFailure(response.StatusCode, membersEndpoint: false);
        }

        var payload = await response.Content.ReadFromJsonAsync<MePayload>(JsonOptions, cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Id))
        {
            throw Unavailable();
        }

        return new VerificationIdentity(
            payload.Id!,
            payload.Username ?? "",
            payload.GlobalName,
            payload.Nickname,
            payload.AvatarUrl);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", Options.ApiKey);
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        try
        {
            // 不记录请求头：API Key 与上游 token 都不能进日志。
            return await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Discord verification request {Path} timed out", path);
            throw Unreachable();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Discord verification request {Path} failed", path);
            throw Unreachable();
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            throw new AppException(
                "verification_not_configured",
                "还没有配置 Discord 验证服务的 API Key",
                StatusCodes.Status503ServiceUnavailable);
        }
    }

    private AppException MapFailure(HttpStatusCode status, bool membersEndpoint)
    {
        logger.LogWarning("Discord verification upstream returned {Status}", (int)status);

        return status switch
        {
            HttpStatusCode.NotFound => new AppException(
                "not_a_member", "你不在班级的 Discord 服务器里", StatusCodes.Status404NotFound),
            HttpStatusCode.TooManyRequests => new AppException(
                "too_frequent", "请求太频繁，请稍后再试", StatusCodes.Status429TooManyRequests),
            HttpStatusCode.BadGateway => new AppException(
                "delivery_failed", "验证码发送失败，请稍后再试", StatusCodes.Status502BadGateway),
            HttpStatusCode.ServiceUnavailable when membersEndpoint => new AppException(
                "members_unavailable", "暂时拿不到成员列表，可以直接输入 Discord 用户 ID", StatusCodes.Status503ServiceUnavailable),
            _ => Unavailable(),
        };
    }

    private static DateTime? ToUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        var date = value.Value;
        return date.Kind switch
        {
            DateTimeKind.Utc => date,
            DateTimeKind.Local => date.ToUniversalTime(),
            _ => DateTime.SpecifyKind(date, DateTimeKind.Utc),
        };
    }

    private static AppException Unavailable() =>
        new("verification_unavailable", "验证服务暂时不可用", StatusCodes.Status503ServiceUnavailable);

    private static AppException Unreachable() =>
        new("verification_unreachable", "连不上验证服务，请稍后再试", StatusCodes.Status502BadGateway);

    private sealed record MembersPayload(int? Count, List<MemberPayload>? Members);

    private sealed record MemberPayload(
        string? Id,
        string? Username,
        string? GlobalName,
        string? Nickname,
        string? AvatarUrl);

    private sealed record RequestPayload(string UserId);

    private sealed record RequestPayloadResponse(string? RequestId, DateTime? ExpiresAt, string? Delivery);

    private sealed record ConfirmPayload(string RequestId, string Code);

    private sealed record ConfirmPayloadResponse(string? AccessToken);

    private sealed record MePayload(
        string? Id,
        string? Username,
        string? GlobalName,
        string? Nickname,
        string? AvatarUrl);
}
