namespace Okisioli.Tools.Buildup.Core;

public sealed class CleanupPlan
{
    internal CleanupPlan(string rootPath, HashSet<string> repositories, HashSet<string> metadata,
        IReadOnlyList<CleanupEntry> entries, long bytesToDelete, IReadOnlyList<string> errors,
        IReadOnlyList<string> skippedRepositories)
    {
        RootPath = rootPath;
        Repositories = repositories;
        RepositoryRoots = Array.AsReadOnly(repositories.OrderBy(path => path.Length).ToArray());
        Metadata = metadata;
        Entries = entries;
        BytesToDelete = bytesToDelete;
        Errors = errors;
        SkippedRepositories = skippedRepositories;
    }

    public string RootPath { get; }
    public IReadOnlyList<string> RepositoryRoots { get; }
    public IReadOnlyList<CleanupEntry> Entries { get; }
    public long BytesToDelete { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> SkippedRepositories { get; }

    public CleanupPlan ForFolder(string folderPath)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        if (!string.Equals(folder, RootPath, comparison)
            && !folder.StartsWith(Path.EndsInDirectorySeparator(RootPath)
                ? RootPath : RootPath + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("The selected folder must be within the scan root.", nameof(folderPath));
        var entries = Entries.Where(entry => string.Equals(entry.Path, folder, comparison)
            || entry.Path.StartsWith(prefix, comparison)).ToArray();
        return new CleanupPlan(RootPath, Repositories, Metadata, Array.AsReadOnly(entries),
            entries.Sum(entry => entry.Bytes), Errors, SkippedRepositories);
    }

    internal HashSet<string> Repositories { get; }
    internal HashSet<string> Metadata { get; }
}
