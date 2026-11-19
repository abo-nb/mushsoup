using System.Text.Json;
using System.Text.Json.Nodes;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using MongoDB.Driver;
using Notes.Contracts;

namespace Notes;

internal sealed class NoteService(MongoDatabaseProvider database, NoteCache cache) : INoteDirectory
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ItemTtl = TimeSpan.FromMinutes(5);

    private IMongoCollection<NoteDocument> Notes => database.Collection<NoteDocument>("notes");

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await Notes.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<NoteDocument>(
                Builders<NoteDocument>.IndexKeys.Ascending(x => x.OwnerId)),
            new CreateIndexModel<NoteDocument>(
                Builders<NoteDocument>.IndexKeys.Ascending(x => x.Visibility).Descending(x => x.UpdatedAt)),
            new CreateIndexModel<NoteDocument>(
                Builders<NoteDocument>.IndexKeys.Ascending(x => x.DeletedAt)),
        ], cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountOwnedByUsersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var f = Builders<NoteDocument>.Filter;
        var owners = await Notes
            .Find(f.And(f.In(x => x.OwnerId, ids), f.Eq(x => x.DeletedAt, (DateTime?)null)))
            .Project(x => x.OwnerId)
            .ToListAsync(cancellationToken);

        return owners
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<NoteSummaryDto>> ListVisibleAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var key = cache.Key($"list:{userId}");
        if (cache.TryGet(key, out IReadOnlyList<NoteSummaryDto>? cached) && cached is not null)
        {
            return cached;
        }

        var f = Builders<NoteDocument>.Filter;
        var visible = f.Or(
            f.Eq(x => x.Visibility, NoteVisibility.Public),
            f.Eq(x => x.OwnerId, userId));

        var notes = await Notes
            .Find(f.And(f.Eq(x => x.DeletedAt, (DateTime?)null), visible))
            .Sort(Builders<NoteDocument>.Sort.Descending(x => x.UpdatedAt))
            .ToListAsync(cancellationToken);

        IReadOnlyList<NoteSummaryDto> result = notes.Select(ToSummary).ToList();
        cache.Set(key, result, ListTtl);
        return result;
    }

    /// <summary>不可见与不存在都返回 null，由端点统一转 404，避免泄露讲义是否存在。</summary>
    public async Task<NoteDto?> GetVisibleAsync(
        string noteId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var key = cache.Key($"item:{userId}:{noteId}");
        if (cache.TryGet(key, out NoteDto? cached))
        {
            return cached;
        }

        var f = Builders<NoteDocument>.Filter;
        var filter = f.And(
            ActiveIdFilter(noteId),
            f.Or(
                f.Eq(x => x.Visibility, NoteVisibility.Public),
                f.Eq(x => x.OwnerId, userId)));

        var note = await Notes.Find(filter).FirstOrDefaultAsync(cancellationToken);
        var dto = note is null ? null : ToDto(note);

        // 不存在的讲义也缓存，避免反复穿透到 Mongo。
        cache.Set<NoteDto?>(key, dto, ItemTtl);
        return dto;
    }

    public async Task<bool> IsPublicAsync(string noteId, CancellationToken cancellationToken = default)
    {
        var f = Builders<NoteDocument>.Filter;
        var count = await Notes
            .CountDocumentsAsync(
                f.And(
                    ActiveIdFilter(noteId),
                    f.Eq(x => x.Visibility, NoteVisibility.Public)),
                cancellationToken: cancellationToken);
        return count > 0;
    }

    public async Task<NoteDto> CreateAsync(
        JsonElement content,
        string userId,
        string? classId,
        CancellationToken cancellationToken = default)
    {
        var payload = NoteValidator.Validate(content);
        var now = DateTime.UtcNow;

        var note = new NoteDocument
        {
            Id = Ids.New(),
            OwnerId = userId,
            ClassId = classId ?? "",
            Title = payload.Title,
            Subtitle = payload.Subtitle,
            Tags = payload.Tags,
            Visibility = payload.Visibility,
            Year = payload.Year,
            Semester = payload.Semester,
            SectionCount = payload.SectionCount,
            ContentJson = payload.ContentJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await Notes.InsertOneAsync(note, cancellationToken: cancellationToken);
        cache.Invalidate();
        return ToDto(note);
    }

    /// <summary>整份替换：保留 CreatedAt，只刷新 UpdatedAt。</summary>
    public async Task<NoteDto> UpdateAsync(
        string noteId,
        JsonElement content,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(noteId, userId, cancellationToken);
        var payload = NoteValidator.Validate(content);

        var updated = await Notes.FindOneAndUpdateAsync(
            ActiveIdFilter(noteId),
            Builders<NoteDocument>.Update
                .Set(x => x.Title, payload.Title)
                .Set(x => x.Subtitle, payload.Subtitle)
                .Set(x => x.Tags, payload.Tags)
                .Set(x => x.Visibility, payload.Visibility)
                .Set(x => x.Year, payload.Year)
                .Set(x => x.Semester, payload.Semester)
                .Set(x => x.SectionCount, payload.SectionCount)
                .Set(x => x.ContentJson, payload.ContentJson)
                .Set(x => x.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<NoteDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        var result = updated ?? throw new NotFoundException("讲义不存在");
        cache.Invalidate();
        return ToDto(result);
    }

    /// <summary>只改元信息：可见性、标签、学年学期，并同步 ContentJson 里的同名字段。</summary>
    public async Task<NoteDto> UpdateMetadataAsync(
        string noteId,
        NoteMetadataRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var note = await RequireOwnerAsync(noteId, userId, cancellationToken);

        var visibility = request.Visibility is null
            ? note.Visibility
            : NormalizeVisibility(request.Visibility);
        var tags = request.Tags is null ? note.Tags : NormalizeTags(request.Tags);
        var year = request.Year is null ? note.Year : NormalizeYear(request.Year);
        var semester = request.Semester is null ? note.Semester : NormalizeSemester(request.Semester);

        if (visibility == NoteVisibility.Public && tags.Count == 0)
        {
            throw new ValidationFailedException("tags", "公开讲义至少需要一个标签");
        }

        var updated = await Notes.FindOneAndUpdateAsync(
            ActiveIdFilter(noteId),
            Builders<NoteDocument>.Update
                .Set(x => x.Tags, tags)
                .Set(x => x.Visibility, visibility)
                .Set(x => x.Year, year)
                .Set(x => x.Semester, semester)
                .Set(x => x.ContentJson, SyncContentMetadata(note.ContentJson, tags, visibility, year, semester))
                .Set(x => x.UpdatedAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<NoteDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

        var result = updated ?? throw new NotFoundException("讲义不存在");
        cache.Invalidate();
        return ToDto(result);
    }

    public async Task DeleteAsync(string noteId, string userId, CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(noteId, userId, cancellationToken);
        var now = DateTime.UtcNow;

        await Notes.UpdateOneAsync(
            ActiveIdFilter(noteId),
            Builders<NoteDocument>.Update
                .Set(x => x.DeletedAt, now)
                .Set(x => x.UpdatedAt, now),
            cancellationToken: cancellationToken);

        cache.Invalidate();
    }

    private async Task<NoteDocument> RequireOwnerAsync(string noteId, string userId, CancellationToken cancellationToken)
    {
        var note = await Notes.Find(ActiveIdFilter(noteId)).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("讲义不存在");

        if (note.OwnerId != userId)
        {
            throw new ForbiddenException("只有讲义创建者可以执行此操作");
        }

        return note;
    }

    private static string NormalizeVisibility(string visibility)
    {
        var trimmed = visibility.Trim();
        if (trimmed != NoteVisibility.Public && trimmed != NoteVisibility.Private)
        {
            throw new ValidationFailedException("visibility", "可见性只能是 public 或 private");
        }

        return trimmed;
    }

    private static string NormalizeYear(string year)
    {
        var trimmed = year.Trim();
        if (trimmed.Length > NoteValidator.MaxYearLength)
        {
            throw new ValidationFailedException("year", $"学年不能超过 {NoteValidator.MaxYearLength} 字");
        }

        return trimmed;
    }

    private static string NormalizeSemester(string semester)
    {
        var trimmed = semester.Trim();
        if (trimmed.Length > 0 && trimmed != "上" && trimmed != "下")
        {
            throw new ValidationFailedException("semester", "学期只能是上或下");
        }

        return trimmed;
    }

    /// <summary>trim、去空、忽略大小写去重；数量与长度超限抛 422。</summary>
    private static List<string> NormalizeTags(IReadOnlyList<string> tags)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            var tag = raw?.Trim();
            if (string.IsNullOrEmpty(tag))
            {
                continue;
            }

            if (tag.Length > NoteValidator.MaxTagLength)
            {
                throw new ValidationFailedException("tags", $"每个标签不能超过 {NoteValidator.MaxTagLength} 字");
            }

            if (seen.Add(tag))
            {
                normalized.Add(tag);
            }
        }

        if (normalized.Count > NoteValidator.MaxTagCount)
        {
            throw new ValidationFailedException("tags", $"标签最多 {NoteValidator.MaxTagCount} 个");
        }

        return normalized;
    }

    /// <summary>阅读页读的是 contentJson，必须和集合里的元信息保持一致。</summary>
    private static string SyncContentMetadata(
        string contentJson,
        IReadOnlyList<string> tags,
        string visibility,
        string year,
        string semester)
    {
        if (JsonNode.Parse(contentJson) is not JsonObject content)
        {
            return contentJson;
        }

        var tagArray = new JsonArray();
        foreach (var tag in tags)
        {
            tagArray.Add(tag);
        }

        content["tags"] = tagArray;
        content["visibility"] = visibility;
        content["year"] = year;
        content["semester"] = semester;
        return content.ToJsonString();
    }

    private static FilterDefinition<NoteDocument> ActiveIdFilter(string noteId)
    {
        var f = Builders<NoteDocument>.Filter;
        return f.And(f.Eq(x => x.Id, noteId), f.Eq(x => x.DeletedAt, (DateTime?)null));
    }

    private static NoteSummaryDto ToSummary(NoteDocument note) => new(
        note.Id,
        note.OwnerId,
        note.Title,
        note.Subtitle,
        note.Tags,
        note.Visibility,
        note.Year,
        note.Semester,
        note.SectionCount,
        note.CreatedAt,
        note.UpdatedAt,
        NoteChapters.FromJson(note.ContentJson));

    private static NoteDto ToDto(NoteDocument note)
    {
        using var document = JsonDocument.Parse(note.ContentJson);
        return new NoteDto(
            note.Id,
            note.OwnerId,
            note.Title,
            note.Subtitle,
            note.Tags,
            note.Visibility,
            note.Year,
            note.Semester,
            document.RootElement.Clone(),
            note.CreatedAt,
            note.UpdatedAt,
            NoteChapters.FromContent(document.RootElement));
    }
}
