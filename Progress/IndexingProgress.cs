namespace CorporateRag.Progress;

public sealed record IndexingProgressSnapshot(
    bool IsIndexing,
    string? FileName,
    int Current,
    int Total,
    string Message,
    string? Error,
    DateTimeOffset? StartedAtUtc)
{
    public int Percent => Total <= 0 ? 0 : Math.Clamp((int)Math.Round(Current * 100.0 / Total), 0, 100);
    public int ElapsedSeconds => StartedAtUtc is null
        ? 0
        : Math.Max(0, (int)Math.Round((DateTimeOffset.UtcNow - StartedAtUtc.Value).TotalSeconds));

    public int? EstimatedRemainingSeconds
    {
        get
        {
            if (!IsIndexing || Total <= 0 || Current <= 0 || StartedAtUtc is null)
            {
                return null;
            }

            var elapsed = Math.Max(1, (DateTimeOffset.UtcNow - StartedAtUtc.Value).TotalSeconds);
            var secondsPerItem = elapsed / Current;
            return Math.Max(0, (int)Math.Round((Total - Current) * secondsPerItem));
        }
    }
}

public interface IIndexingProgress
{
    IndexingProgressSnapshot Snapshot { get; }
    void Start(string fileName, int total);
    void Preparing(string fileName, string message);
    void Advance(int current, string message);
    void Complete(string message);
    void Fail(string message);
}

public sealed class IndexingProgress : IIndexingProgress
{
    private readonly object _gate = new();
    private IndexingProgressSnapshot _snapshot = new(
        IsIndexing: false,
        FileName: null,
        Current: 0,
        Total: 0,
        Message: "Waiting for PDF",
        Error: null,
        StartedAtUtc: null);

    public IndexingProgressSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Start(string fileName, int total)
    {
        lock (_gate)
        {
            _snapshot = new IndexingProgressSnapshot(
                IsIndexing: true,
                FileName: fileName,
                Current: 0,
                Total: total,
                Message: "Preparing chunks",
                Error: null,
                StartedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    public void Preparing(string fileName, string message)
    {
        lock (_gate)
        {
            _snapshot = new IndexingProgressSnapshot(
                IsIndexing: true,
                FileName: fileName,
                Current: 0,
                Total: 0,
                Message: message,
                Error: null,
                StartedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    public void Advance(int current, string message)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsIndexing = true,
                Current = Math.Clamp(current, 0, _snapshot.Total),
                Message = message,
                Error = null
            };
        }
    }

    public void Complete(string message)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsIndexing = false,
                Current = _snapshot.Total,
                Message = message,
                Error = null
            };
        }
    }

    public void Fail(string message)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsIndexing = false,
                Message = message,
                Error = message
            };
        }
    }
}
