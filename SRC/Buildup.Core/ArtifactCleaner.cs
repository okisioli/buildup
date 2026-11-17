using System.Diagnostics;

namespace Buildup.Core;

public sealed class ArtifactCleaner
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public CleanupPlan Scan(string rootPath, Action<string>? onScanning = null,
        Action<string, int, int>? onProgress = null, Action<string>? onDetail = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        if (IsLink(root))
            throw new ArgumentException("The scan root must not be a symbolic link or junction.", nameof(rootPath));

        var errors = new List<string>();
        var skippedRepositories = new List<string>();
        var candidates = new HashSet<string>(PathComparer);
        var repositories = new HashSet<string>(PathComparer);
        var metadata = new HashSet<string>(PathComparer);
        // Only explicit working-tree roots at or below the scan root are eligible.
        // In particular, an ancestor repository must not turn Downloads into a cleanup target.
        Discover(root, candidates, metadata, errors, onScanning);
        var entries = new List<CleanupEntry>();
        long bytesToDelete = 0;
        var files = 0;
        var directories = 0;
        var lastDetail = Stopwatch.GetTimestamp();

        void ReportDetail(string repository, string activity, string? path = null, bool force = false)
        {
            if (onDetail is null || (!force && Stopwatch.GetElapsedTime(lastDetail).TotalMilliseconds < 100))
                return;
            lastDetail = Stopwatch.GetTimestamp();
            var name = Path.GetRelativePath(root, repository);
            if (name == ".")
                name = new DirectoryInfo(root).Name;
            var location = path is null ? "" : $": {Path.GetRelativePath(repository, path)}";
            onDetail($"{name}\n  {activity}{location}\n  {files:N0} files, {directories:N0} folders eligible; {bytesToDelete:N0} bytes");
        }

        var validated = 0;
        foreach (var repository in candidates)
        {
            onProgress?.Invoke("Validating repositories", ++validated, candidates.Count);
            ReportDetail(repository, "Checking working-tree root", force: true);
            var topLevel = Git(repository, "rev-parse", "--show-toplevel");
            if (topLevel.ExitCode != 0 || !PathComparer.Equals(repository,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(topLevel.Output.TrimEnd('\r', '\n')))))
            {
                // Keep rejected roots protected even when an ancestor repository ignores them.
                metadata.Add(repository);
                var reason = topLevel.ExitCode != 0 ? topLevel.Error.Trim() : "not a Git working-tree root";
                skippedRepositories.Add($"Skipping folder {repository}: {reason}");
                continue;
            }

            var valid = true;
            foreach (var option in new[] { "--absolute-git-dir", "--git-common-dir" })
            {
                ReportDetail(repository, option == "--absolute-git-dir"
                    ? "Locating Git metadata" : "Locating shared Git metadata", force: true);
                var location = Git(repository, "rev-parse", "--path-format=absolute", option);
                if (location.ExitCode != 0)
                {
                    metadata.Add(repository);
                    skippedRepositories.Add($"Skipping folder {repository}: {location.Error.Trim()}");
                    valid = false;
                    break;
                }
                metadata.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Output.TrimEnd('\r', '\n'))));
            }
            if (valid)
                repositories.Add(repository);
        }
        var calculated = 0;
        foreach (var repository in repositories.Order(PathComparer))
        {
            onProgress?.Invoke("Calculating results", ++calculated, repositories.Count);
            try
            {
                // Git supplies untracked ignored paths, so tracked files are never candidates.
                ReportDetail(repository, "Asking Git for ignored files", force: true);
                var ignoredFiles = IgnoredPaths(repository, false);
                ReportDetail(repository, "Asking Git for ignored folders", force: true);
                var ignoredDirectories = IgnoredPaths(repository, true);
                var directoryCandidates = new HashSet<string>(PathComparer);
                var removed = new HashSet<string>(PathComparer);
                var safeDirectories = new Dictionary<string, bool>(PathComparer);

                ReportDetail(repository, $"Measuring {ignoredFiles.Length:N0} ignored files", force: true);
                foreach (var path in ignoredFiles)
                {
                    ReportDetail(repository, "Measuring ignored files", path);
                    try
                    {
                        if (!IsSafe(path, false, repository, metadata, safeDirectories) || !File.Exists(path))
                            continue;
                        var size = new FileInfo(path).Length;
                        removed.Add(path);
                        entries.Add(new CleanupEntry(path, false, size));
                        files++;
                        bytesToDelete += size;
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        errors.Add($"{path}: {ex.Message}");
                    }
                }

                ReportDetail(repository, "Finding ignored subfolders", force: true);
                foreach (var path in ignoredDirectories.Where(Directory.Exists))
                    CollectDirectories(path, repository, metadata, safeDirectories, directoryCandidates, errors,
                        folder => ReportDetail(repository, "Finding ignored subfolders", folder));

                ReportDetail(repository, $"Checking {directoryCandidates.Count:N0} artifact folders", force: true);
                foreach (var path in directoryCandidates.OrderByDescending(path => path.Length))
                {
                    ReportDetail(repository, "Checking artifact folders", path);
                    try
                    {
                        // Never recursively delete: unexpected files or concurrent writes keep the directory alive.
                        if (Directory.EnumerateFileSystemEntries(path).Any(child => !removed.Contains(child)))
                            continue;
                        removed.Add(path);
                        entries.Add(new CleanupEntry(path, true));
                        directories++;
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        errors.Add($"{path}: {ex.Message}");
                    }
                }
                ReportDetail(repository, "Repository calculation complete", force: true);
            }
            catch (Exception ex) when (IsFileSystemError(ex) || ex is InvalidOperationException)
            {
                errors.Add($"{repository}: {ex.Message}");
            }
        }

        return new CleanupPlan(root, repositories, metadata, entries.AsReadOnly(), bytesToDelete,
            errors.AsReadOnly(), skippedRepositories.AsReadOnly());
    }

    public CleanupResult Delete(CleanupPlan plan, Action<string>? onRemoving = null,
        Action<CleanupEntry>? onDeleted = null)
    {
        var errors = new List<string>();
        var files = 0;
        var directories = 0;
        long bytesDeleted = 0;
        var reported = new HashSet<string>(PathComparer);
        foreach (var repository in plan.Repositories.Order(PathComparer))
        {
            var entries = plan.Entries.Where(entry => IsWithin(entry.Path, repository)).ToArray();
            if (entries.Length == 0)
                continue;
            try
            {
                // Recheck ignore rules and tracked status after confirmation. Only approved paths are eligible.
                var ignored = new HashSet<string>(IgnoredPaths(repository, false), PathComparer);
                foreach (var entry in entries)
                {
                    try
                    {
                        if (!IsSafe(entry.Path, entry.IsDirectory, repository, plan.Metadata))
                            continue;
                        if (entry.IsDirectory)
                        {
                            if (!Directory.Exists(entry.Path) || Directory.EnumerateFileSystemEntries(entry.Path).Any())
                                continue;
                            if (reported.Add(entry.Path))
                                onRemoving?.Invoke(entry.Path);
                            // Never recursively delete: new or preserved files keep the folder alive.
                            Directory.Delete(entry.Path);
                            directories++;
                            onDeleted?.Invoke(entry);
                        }
                        else if (ignored.Contains(entry.Path) && File.Exists(entry.Path))
                        {
                            var folder = Path.GetDirectoryName(entry.Path)!;
                            if (reported.Add(folder))
                                onRemoving?.Invoke(folder);
                            var size = new FileInfo(entry.Path).Length;
                            File.Delete(entry.Path);
                            bytesDeleted += size;
                            files++;
                            onDeleted?.Invoke(entry with { Bytes = size });
                        }
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        errors.Add($"{entry.Path}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (IsFileSystemError(ex) || ex is InvalidOperationException)
            {
                errors.Add($"{repository}: {ex.Message}");
            }
        }
        return new CleanupResult(files, directories, bytesDeleted, errors.AsReadOnly());
    }

    private static void Discover(string path, HashSet<string> repositories, HashSet<string> metadata,
        List<string> errors, Action<string>? onScanning)
    {
        onScanning?.Invoke(path);
        try
        {
            if (HasGitMarker(path))
            {
                repositories.Add(path);
                return;
            }
            // Bare repositories have no worktree to clean. Do not enter their object database.
            if (IsGitDirectory(path))
            {
                metadata.Add(path);
                return;
            }
            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                if (!IsGitMetadata(directory) && !IsLink(directory))
                    Discover(directory, repositories, metadata, errors, onScanning);
            }
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            errors.Add($"{path}: {ex.Message}");
        }
    }

    private static string[] IgnoredPaths(string repository, bool directories)
    {
        var arguments = new List<string> { "ls-files", "--others", "--ignored", "--exclude-standard", "-z" };
        if (directories)
            arguments.Add("--directory");
        var result = Git(repository, arguments.ToArray());
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.Error.Trim());
        return result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(repository, path))))
            .ToArray();
    }

    private static void CollectDirectories(string path, string repository, HashSet<string> metadata,
        Dictionary<string, bool> safeDirectories, HashSet<string> candidates, List<string> errors,
        Action<string>? onDirectory = null)
    {
        try
        {
            if (!IsSafe(path, true, repository, metadata, safeDirectories) || !candidates.Add(path))
                return;
            onDirectory?.Invoke(path);
            foreach (var child in Directory.EnumerateDirectories(path))
                CollectDirectories(child, repository, metadata, safeDirectories, candidates, errors, onDirectory);
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            errors.Add($"{path}: {ex.Message}");
        }
    }

    private static bool IsSafe(string path, bool isDirectory, string repository, HashSet<string> metadata,
        Dictionary<string, bool>? safeDirectories = null)
    {
        if (!IsWithin(path, repository))
            return false;
        if (metadata.Any(directory => PathComparer.Equals(path, directory) || IsWithin(path, directory)))
            return false;
        return isDirectory ? IsSafeDirectory(path) : IsSafeDirectory(Path.GetDirectoryName(path)!)
            && !IsGitMetadata(path) && !IsLink(path);

        bool IsSafeDirectory(string directory)
        {
            if (safeDirectories is not null && safeDirectories.TryGetValue(directory, out var cached))
                return cached;

            // Check parents first so paths through junctions are never probed for Git markers.
            var safe = PathComparer.Equals(directory, repository)
                ? !IsGitMetadata(directory) && !IsLink(directory)
                : IsSafeDirectory(Path.GetDirectoryName(directory)!) && !IsGitMetadata(directory)
                    && !IsLink(directory) && !HasGitMarker(directory) && !IsGitDirectory(directory);
            if (safeDirectories is not null)
                safeDirectories[directory] = safe;
            return safe;
        }
    }

    private static bool HasGitMarker(string directory)
    {
        try
        {
            File.GetAttributes(Path.Combine(directory, ".git"));
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static bool IsGitDirectory(string path) => File.Exists(Path.Combine(path, "HEAD"))
        && ((Directory.Exists(Path.Combine(path, "objects")) && Directory.Exists(Path.Combine(path, "refs")))
            || File.Exists(Path.Combine(path, "commondir")));

    private static bool IsWithin(string path, string parent) =>
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsGitMetadata(string path) =>
        string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase);

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsFileSystemError(Exception ex) => ex is IOException or UnauthorizedAccessException;

    private static (int ExitCode, string Output, string Error) Git(string directory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        // Ambient Git overrides must not redirect reads to another working tree or index.
        foreach (var name in startInfo.Environment.Keys
            .Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            startInfo.Environment.Remove(name);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start Git.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
