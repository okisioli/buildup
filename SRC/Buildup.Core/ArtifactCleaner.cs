using System.Diagnostics;

namespace Buildup.Core;

public sealed class ArtifactCleaner
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public CleanupResult Clean(string rootPath, bool dryRun = false, Action<CleanupEntry>? onEntry = null)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        if (IsLink(root))
            throw new ArgumentException("The scan root must not be a symbolic link or junction.", nameof(rootPath));

        var errors = new List<string>();
        var repositories = new HashSet<string>(PathComparer);
        var metadata = new HashSet<string>(PathComparer);
        // Only explicit working-tree roots at or below the scan root are eligible.
        // In particular, an ancestor repository must not turn Downloads into a cleanup target.
        Discover(root, repositories, metadata, errors);
        foreach (var repository in repositories)
        {
            var topLevel = Git(repository, "rev-parse", "--show-toplevel");
            if (topLevel.ExitCode != 0 || !PathComparer.Equals(repository,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(topLevel.Output.TrimEnd('\r', '\n')))))
                throw new InvalidOperationException($"Not a Git working-tree root: {repository}");

            foreach (var option in new[] { "--absolute-git-dir", "--git-common-dir" })
            {
                var location = Git(repository, "rev-parse", "--path-format=absolute", option);
                if (location.ExitCode != 0)
                    throw new InvalidOperationException(location.Error.Trim());
                metadata.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Output.TrimEnd('\r', '\n'))));
            }
        }
        var files = 0;
        var directories = 0;
        foreach (var repository in repositories.Order(PathComparer))
        {
            try
            {
                // Git supplies untracked ignored paths, so tracked files are never candidates.
                var ignoredFiles = IgnoredPaths(repository, false);
                var ignoredDirectories = IgnoredPaths(repository, true);
                var directoryCandidates = new HashSet<string>(PathComparer);
                var removed = new HashSet<string>(PathComparer);

                foreach (var path in ignoredFiles)
                {
                    if (!IsSafe(path, root, repository, repositories, metadata) || !File.Exists(path))
                        continue;
                    try
                    {
                        if (!dryRun)
                            File.Delete(path);
                        removed.Add(path);
                        files++;
                        onEntry?.Invoke(new CleanupEntry(path, false));
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        errors.Add($"{path}: {ex.Message}");
                    }
                }

                foreach (var path in ignoredDirectories.Where(Directory.Exists))
                    CollectDirectories(path, root, repository, repositories, metadata, directoryCandidates, errors);

                foreach (var path in directoryCandidates.OrderByDescending(path => path.Length))
                {
                    if (!IsSafe(path, root, repository, repositories, metadata))
                        continue;
                    try
                    {
                        // Never recursively delete: unexpected files or concurrent writes keep the directory alive.
                        if (Directory.EnumerateFileSystemEntries(path).Any(child => !dryRun || !removed.Contains(child)))
                            continue;
                        if (!dryRun)
                            Directory.Delete(path);
                        removed.Add(path);
                        directories++;
                        onEntry?.Invoke(new CleanupEntry(path, true));
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        errors.Add($"{path}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (IsFileSystemError(ex) || ex is InvalidOperationException)
            {
                errors.Add($"{repository}: {ex.Message}");
            }
        }

        return new CleanupResult(files, directories, errors.AsReadOnly());
    }

    private static void Discover(string path, HashSet<string> repositories, HashSet<string> metadata, List<string> errors)
    {
        try
        {
            if (File.Exists(Path.Combine(path, ".git")) || Directory.Exists(Path.Combine(path, ".git")))
                repositories.Add(path);
            // Bare repositories have no worktree to clean. Do not enter their object database.
            if (File.Exists(Path.Combine(path, "HEAD")) && Directory.Exists(Path.Combine(path, "objects"))
                && Directory.Exists(Path.Combine(path, "refs")))
            {
                metadata.Add(path);
                return;
            }
            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                if (!IsGitMetadata(directory) && !IsLink(directory))
                    Discover(directory, repositories, metadata, errors);
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

    private static void CollectDirectories(string path, string root, string repository,
        HashSet<string> repositories, HashSet<string> metadata, HashSet<string> candidates, List<string> errors)
    {
        if (!IsSafe(path, root, repository, repositories, metadata))
            return;
        try
        {
            candidates.Add(path);
            foreach (var child in Directory.EnumerateDirectories(path))
                CollectDirectories(child, root, repository, repositories, metadata, candidates, errors);
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            errors.Add($"{path}: {ex.Message}");
        }
    }

    private static bool IsSafe(string path, string root, string repository, HashSet<string> repositories,
        HashSet<string> metadata)
    {
        if (!PathComparer.Equals(repository, root) && !IsWithin(repository, root))
            return false;
        if (!IsWithin(path, root) || !IsWithin(path, repository))
            return false;
        if (metadata.Any(directory => PathComparer.Equals(path, directory) || IsWithin(path, directory)))
            return false;
        if (repositories.Any(other => IsWithin(other, repository)
            && (PathComparer.Equals(path, other) || IsWithin(path, other))))
            return false;
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if (IsGitMetadata(current) || IsLink(current))
                return false;
            if (PathComparer.Equals(current, repository))
                return true;
        }
        return false;
    }

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
