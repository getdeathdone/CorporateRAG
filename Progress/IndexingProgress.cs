namespace CorporateRag.Progress;

public sealed record IndexingProgressSnapshot(
    bool IsIndexing,
    string? FileName,
    int Current,
    int Total,
    string Message,
    string? Error)
{
    public int Percent => Total <= 0 ? 0 : Math.Clamp((int)Math.Round(Current * 100.0 / Total), 0, 100);
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
        Error: null);

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
                Error: null);
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
                Error: null);
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
