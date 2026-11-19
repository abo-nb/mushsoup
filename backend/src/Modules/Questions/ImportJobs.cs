using System.Collections.Concurrent;
using BuildingBlocks.Contracts;
using Questions.Contracts;

namespace Questions;

internal sealed record CachedImportPlan(
    List<ImportCandidate> Candidates,
    List<ImportIssueDto> BatchIssues,
    ImportBankPlan? Bank = null,
    List<ImportPoolPlan>? Pools = null);

internal sealed class ImportPlanCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private const int MaxEntries = 4;

    private readonly ConcurrentDictionary<string, Entry> plans = new(StringComparer.Ordinal);

    public string Store(string userId, string bankId, CachedImportPlan plan)
    {
        var planId = Ids.New();
        plans[planId] = new Entry(userId, bankId, DateTime.UtcNow, plan);
        Cleanup();
        return planId;
    }

    public CachedImportPlan? Take(string planId, string userId, string bankId)
    {
        Cleanup();

        if (!plans.TryGetValue(planId, out var entry))
        {
            return null;
        }

        if (entry.UserId != userId || entry.BankId != bankId || entry.CreatedAt < DateTime.UtcNow - Ttl)
        {
            return null;
        }

        return plans.TryRemove(planId, out var taken) ? taken.Plan : null;
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - Ttl;
        foreach (var pair in plans)
        {
            if (pair.Value.CreatedAt < cutoff)
            {
                plans.TryRemove(pair.Key, out _);
            }
        }

        var overflow = plans.Count - MaxEntries;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var pair in plans.OrderBy(x => x.Value.CreatedAt).Take(overflow))
        {
            plans.TryRemove(pair.Key, out _);
        }
    }

    private sealed record Entry(string UserId, string BankId, DateTime CreatedAt, CachedImportPlan Plan);
}

internal sealed class ImportJob
{
    private readonly object gate = new();

    private string status = ImportJobStatus.Running;
    private int total;
    private int processed;
    private int imported;
    private int skipped;
    private int failed;
    private int pending;
    private string? batchId;
    private string? error;
    private DateTime? finishedAt;

    public ImportJob(string userId, string bankId)
    {
        Id = Ids.New();
        UserId = userId;
        BankId = bankId;
        CreatedAt = DateTime.UtcNow;
    }

    public string Id { get; }

    public string UserId { get; }

    public string BankId { get; }

    public DateTime CreatedAt { get; }

    public void Begin(int count)
    {
        lock (gate)
        {
            total = count;
        }
    }

    public void Report(int processed, int imported, int skipped, int failed, int pending)
    {
        lock (gate)
        {
            this.processed = processed;
            this.imported = imported;
            this.skipped = skipped;
            this.failed = failed;
            this.pending = pending;
        }
    }

    public void Complete(string batchId)
    {
        lock (gate)
        {
            this.batchId = batchId;
            processed = total;
            status = ImportJobStatus.Done;
            finishedAt = DateTime.UtcNow;
        }
    }

    public void Fail(string message)
    {
        lock (gate)
        {
            status = ImportJobStatus.Failed;
            error = message;
            finishedAt = DateTime.UtcNow;
        }
    }

    public ImportJobDto ToDto()
    {
        lock (gate)
        {
            return new ImportJobDto(
                Id,
                status,
                total,
                processed,
                imported,
                skipped,
                failed,
                pending,
                batchId,
                error,
                CreatedAt,
                finishedAt);
        }
    }
}

internal sealed class ImportJobStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);
    private const int MaxEntries = 64;

    private readonly ConcurrentDictionary<string, ImportJob> jobs = new(StringComparer.Ordinal);

    public ImportJob Create(string userId, string bankId)
    {
        var job = new ImportJob(userId, bankId);
        jobs[job.Id] = job;
        Cleanup();
        return job;
    }

    public ImportJob? Get(string jobId)
    {
        Cleanup();

        if (!jobs.TryGetValue(jobId, out var job))
        {
            return null;
        }

        return job.CreatedAt < DateTime.UtcNow - Ttl ? null : job;
    }

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow - Ttl;
        foreach (var pair in jobs)
        {
            if (pair.Value.CreatedAt < cutoff)
            {
                jobs.TryRemove(pair.Key, out _);
            }
        }

        var overflow = jobs.Count - MaxEntries;
        if (overflow <= 0)
        {
            return;
        }

        foreach (var pair in jobs.OrderBy(x => x.Value.CreatedAt).Take(overflow))
        {
            jobs.TryRemove(pair.Key, out _);
        }
    }
}
