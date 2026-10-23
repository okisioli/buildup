using Buildup.Core;
using Spectre.Console;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: buildup");
    Console.WriteLine("Deletes ignored files only in Git repositories rooted at or below the current directory.");
    Console.WriteLine("Lists scanned folders and cleanup folders, then asks for confirmation before deleting.");
    return 0;
}

if (args.Length > 0)
{
    Console.Error.WriteLine("Usage: buildup");
    return 2;
}

try
{
    var cleaner = new ArtifactCleaner();
    var root = Directory.GetCurrentDirectory();
    var animate = !Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Ansi
        && AnsiConsole.Profile.Capabilities.Interactive;
    var plan = Scan(cleaner, root, animate);
    if (plan.SkippedRepositories.Count > 0)
        Console.WriteLine();
    foreach (var skipped in plan.SkippedRepositories)
    {
        if (animate)
            AnsiConsole.MarkupLine($"[yellow]SKIP[/] [grey]{Markup.Escape(skipped)}[/]");
        else
            Console.WriteLine($"SKIP {skipped}");
    }
    if (plan.Errors.Count > 0)
        Console.WriteLine();
    foreach (var error in plan.Errors)
        Console.Error.WriteLine(error);
    Console.WriteLine();
    if (plan.Entries.Count == 0)
    {
        Console.WriteLine("No folders to clean.");
        return plan.Errors.Count == 0 ? 0 : 1;
    }

    Console.WriteLine("Folders to clean (3 levels; sizes include ignored files in all subfolders):");
    PrintCleanupFolders(plan, animate);

    Console.WriteLine();
    Console.WriteLine($"Estimated disk space savings: {FormatSize(plan.BytesToDelete)}.");
    Console.WriteLine();
    Console.Write("Delete these folders? (y/n) ");
    if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Deletion cancelled.");
        return plan.Errors.Count == 0 ? 0 : 1;
    }

    var result = Delete(cleaner, plan, animate);
    foreach (var error in result.Errors)
        Console.Error.WriteLine(error);
    Console.WriteLine($"Disk space saved: {FormatSize(result.BytesDeleted)}.");
    return plan.Errors.Count == 0 && result.Errors.Count == 0 ? 0 : 1;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
    or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static CleanupPlan Scan(ArtifactCleaner cleaner, string root, bool animate)
{
    var folders = 0;
    if (!animate)
    {
        string? lastPhase = null;
        var plainPlan = cleaner.Scan(root, folder =>
        {
            folders++;
            if (FolderDepth(root, folder) <= 2)
                Console.WriteLine($"Scanning: {FormatFolder(root, folder)}");
        }, (phase, _, _) =>
        {
            if (phase != lastPhase)
            {
                Console.WriteLine($"{phase}...");
                lastPhase = phase;
            }
        });
        Console.WriteLine($"Scan complete: {folders} folders scanned.");
        return plainPlan;
    }

    var plan = AnsiConsole.Status().Spinner(Spinner.Known.Line).Start("Scanning for Git repositories...", context =>
        cleaner.Scan(root, folder =>
        {
            folders++;
            context.Status($"Scanning ({folders} folders): {Markup.Escape(Path.GetRelativePath(root, DisplayFolder(root, folder)))}");
        }, (phase, current, total) => context.Status($"{phase} ({current}/{total})...")));
    Console.WriteLine($"Scan complete: {folders} folders scanned.");
    return plan;
}

static CleanupResult Delete(ArtifactCleaner cleaner, CleanupPlan plan, bool animate)
{
    if (!animate)
    {
        var reported = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        return cleaner.Delete(plan, folder =>
        {
            var displayed = DisplayFolder(plan.RootPath, folder);
            if (reported.Add(displayed))
                Console.WriteLine($"Removing: {FormatFolder(plan.RootPath, displayed)}");
        });
    }

    var result = AnsiConsole.Progress().AutoClear(true).Columns(
        new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn())
        .Start(context =>
        {
            var task = context.AddTask("Revalidating cleanup...", maxValue: plan.Entries.Count);
            var deleted = 0;
            var cleanupResult = cleaner.Delete(plan,
                folder => task.Description = $"Removing: {Markup.Escape(Path.GetRelativePath(plan.RootPath, DisplayFolder(plan.RootPath, folder)))}",
                _ =>
                {
                    deleted++;
                    task.Increment(1);
                    task.Description = $"Deleted {deleted} of {plan.Entries.Count} items";
                });
            task.StopTask();
            return cleanupResult;
        });
    Console.WriteLine($"Deleted {result.Files} files and {result.Directories} directories.");
    return result;
}

static void PrintCleanupFolders(CleanupPlan plan, bool highlight)
{
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var sizes = new Dictionary<string, long>(comparer);
    foreach (var entry in plan.Entries)
    {
        var folder = DisplayFolder(plan.RootPath, entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path)!);
        while (true)
        {
            sizes[folder] = sizes.GetValueOrDefault(folder) + entry.Bytes;
            if (comparer.Equals(folder, plan.RootPath))
                break;
            folder = Path.GetDirectoryName(folder)!;
        }
    }

    var children = sizes.Keys.Where(folder => !comparer.Equals(folder, plan.RootPath))
        .ToLookup(folder => Path.GetDirectoryName(folder)!, comparer);
    WriteFolder(plan.RootPath);

    void WriteFolder(string folder)
    {
        var label = FormatFolder(plan.RootPath, folder);
        var size = FormatSize(sizes[folder]);
        if (highlight)
            AnsiConsole.MarkupLine($"{Markup.Escape(label)}  [bold yellow]{size}[/]");
        else
            Console.WriteLine($"{label}  {size}");
        foreach (var child in children[folder].Order(comparer))
            WriteFolder(child);
    }
}

static string[] FolderParts(string root, string folder) => Path.GetRelativePath(root, folder)
    .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

static int FolderDepth(string root, string folder) =>
    Path.GetRelativePath(root, folder) == "." ? 0 : FolderParts(root, folder).Length;

static string DisplayFolder(string root, string folder) => FolderDepth(root, folder) <= 2
    ? folder : Path.Combine(root, Path.Combine(FolderParts(root, folder).Take(2).ToArray()));

static string FormatFolder(string root, string folder) =>
    new string(' ', FolderDepth(root, folder) * 2) + new DirectoryInfo(folder).Name;

static string FormatSize(long bytes)
{
    string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
    var size = (double)bytes;
    var unit = 0;
    while (size >= 1024 && unit < units.Length - 1)
    {
        size /= 1024;
        unit++;
    }
    return $"{size:0.##} {units[unit]}";
}
