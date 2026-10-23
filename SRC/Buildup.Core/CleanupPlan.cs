namespace Buildup.Core;

public sealed class CleanupPlan
{
    internal CleanupPlan(string rootPath, HashSet<string> repositories, HashSet<string> metadata,
        IReadOnlyList<CleanupEntry> entries, long bytesToDelete, IReadOnlyList<string> errors,
        IReadOnlyList<string> skippedRepositories)
    {
        RootPath = rootPath;
        Repositories = repositories;
        Metadata = metadata;
        Entries = entries;
        BytesToDelete = bytesToDelete;
        Errors = errors;
        SkippedRepositories = skippedRepositories;
    }

    public string RootPath { get; }
    public IReadOnlyList<CleanupEntry> Entries { get; }
    public long BytesToDelete { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> SkippedRepositories { get; }
    internal HashSet<string> Repositories { get; }
    internal HashSet<string> Metadata { get; }
}
