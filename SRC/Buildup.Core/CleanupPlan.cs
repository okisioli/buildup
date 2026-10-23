namespace Buildup.Core;

public sealed class CleanupPlan
{
    internal CleanupPlan(string rootPath, HashSet<string> repositories, HashSet<string> metadata,
        IReadOnlyList<CleanupEntry> entries, long bytesToDelete, IReadOnlyList<string> errors)
    {
        RootPath = rootPath;
        Repositories = repositories;
        Metadata = metadata;
        Entries = entries;
        BytesToDelete = bytesToDelete;
        Errors = errors;
    }

    public string RootPath { get; }
    public IReadOnlyList<CleanupEntry> Entries { get; }
    public long BytesToDelete { get; }
    public IReadOnlyList<string> Errors { get; }
    internal HashSet<string> Repositories { get; }
    internal HashSet<string> Metadata { get; }
}
