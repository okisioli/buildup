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
    foreach (var error in plan.Errors)
        Console.Error.WriteLine(error);
    if (plan.Entries.Count == 0)
    {
        Console.WriteLine("No folders to clean.");
        return plan.Errors.Count == 0 ? 0 : 1;
    }

    Console.WriteLine("Folders to clean (including folders containing individual ignored files):");
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    foreach (var folder in plan.Entries.Select(entry => entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path)!)
        .Distinct(comparer).Order(comparer))
        Console.WriteLine(FormatFolder(root, folder));

    Console.WriteLine($"Estimated disk space savings: {FormatSize(plan.BytesToDelete)}.");
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
    if (!animate)
        return cleaner.Scan(root, folder => Console.WriteLine($"Scanning: {FormatFolder(root, folder)}"));

    var folders = 0;
    var plan = AnsiConsole.Status().Spinner(Spinner.Known.Line).Start("Scanning for Git repositories...", context =>
        cleaner.Scan(root, folder =>
        {
            folders++;
            context.Status($"Scanning ({folders} folders): {Markup.Escape(Path.GetRelativePath(root, folder))}");
        }));
    Console.WriteLine($"Scan complete: {folders} folders scanned.");
    return plan;
}

static CleanupResult Delete(ArtifactCleaner cleaner, CleanupPlan plan, bool animate)
{
    if (!animate)
        return cleaner.Delete(plan, folder => Console.WriteLine($"Removing: {FormatFolder(plan.RootPath, folder)}"));

    var result = AnsiConsole.Progress().AutoClear(true).Columns(
        new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn())
        .Start(context =>
        {
            var task = context.AddTask("Revalidating cleanup...", maxValue: plan.Entries.Count);
            var deleted = 0;
            var cleanupResult = cleaner.Delete(plan,
                folder => task.Description = $"Removing: {Markup.Escape(Path.GetRelativePath(plan.RootPath, folder))}",
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

static string FormatFolder(string root, string folder)
{
    var relative = Path.GetRelativePath(root, folder);
    var depth = relative.Count(character => character == Path.DirectorySeparatorChar
        || character == Path.AltDirectorySeparatorChar);
    return new string(' ', depth * 2) + Path.GetFileName(relative);
}

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
