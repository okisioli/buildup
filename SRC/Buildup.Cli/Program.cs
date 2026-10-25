using Buildup.Core;
using Spectre.Console;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: buildup");
    Console.WriteLine("Deletes ignored files in the current or nearest containing Git repository, or repositories below the current directory.");
    Console.WriteLine("Previews cleanup; use Up/Down to select, Delete to clean, and Enter to confirm.");
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
    var currentDirectory = Directory.GetCurrentDirectory();
    var root = FindContainingRepository(currentDirectory) ?? currentDirectory;
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
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var singleRepository = plan.RepositoryRoots.Contains(plan.RootPath, comparer);
    if (singleRepository)
        Console.WriteLine(comparer.Equals(root, currentDirectory)
            ? $"This is a Git repository: {root}"
            : $"Using containing Git repository: {root}");
    if (plan.Entries.Count == 0)
    {
        Console.WriteLine("No folders to clean.");
        return plan.Errors.Count == 0 ? 0 : 1;
    }

    Console.WriteLine($"Estimated disk space savings: {FormatSize(plan.BytesToDelete)}.");
    Console.WriteLine();
    if (!animate || Console.IsInputRedirected)
    {
        PrintCleanupFolders(plan, false);
        Console.WriteLine("Interactive terminal required for keyboard cleanup. Nothing deleted.");
        return plan.Errors.Count == 0 ? 0 : 1;
    }

    var result = PickFolders(cleaner, plan);
    return plan.Errors.Count == 0 && result.Errors.Count == 0 ? 0 : 1;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
    or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static string? FindContainingRepository(string path)
{
    for (DirectoryInfo? folder = new(path); folder is not null; folder = folder.Parent)
    {
        // Do not widen cleanup scope across symbolic links or junctions.
        if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
            return null;
        if (File.Exists(Path.Combine(folder.FullName, ".git"))
            || Directory.Exists(Path.Combine(folder.FullName, ".git")))
            return folder.FullName;
    }
    return null;
}

static CleanupPlan Scan(ArtifactCleaner cleaner, string root, bool animate)
{
    var folders = 0;
    var repositoryBorders = new List<string>();

    string ScanFolder(string folder)
    {
        var border = repositoryBorders.FirstOrDefault(repository => IsAtOrBelow(folder, repository));
        if (border is not null)
            return border;
        if (File.Exists(Path.Combine(folder, ".git")) || Directory.Exists(Path.Combine(folder, ".git")))
            repositoryBorders.Add(folder);
        return folder;
    }

    if (!animate)
    {
        string? lastPhase = null;
        var detailTimer = System.Diagnostics.Stopwatch.StartNew();
        var firstDetail = true;
        var plainPlan = cleaner.Scan(root, folder =>
        {
            folders++;
            if (ScanFolder(folder) == folder && folder != root)
                Console.WriteLine($"Scanning: {FormatFolder(root, folder)}");
        }, (phase, _, _) =>
        {
            firstDetail = true;
            if (phase != lastPhase)
            {
                Console.WriteLine($"{phase}...");
                lastPhase = phase;
            }
        }, detail =>
        {
            if (!firstDetail && detailTimer.ElapsedMilliseconds < 1000)
                return;
            Console.WriteLine($"  {detail}");
            firstDetail = false;
            detailTimer.Restart();
        });
        Console.WriteLine($"Scan complete: {folders} folders scanned.");
        return plainPlan;
    }

    var phaseStatus = "Scanning for Git repositories...";
    var plan = AnsiConsole.Status().Spinner(Spinner.Known.Line).Start(phaseStatus, context =>
        cleaner.Scan(root, folder =>
        {
            folders++;
            context.Status($"Scanning ({folders} folders): {Markup.Escape(Path.GetRelativePath(root, ScanFolder(folder)))}");
        }, (phase, current, total) =>
        {
            phaseStatus = $"{phase} ({current}/{total})...";
            context.Status(phaseStatus);
        }, detail => context.Status($"{phaseStatus}\n  [grey]{Markup.Escape(detail)}[/]")));
    Console.WriteLine($"Scan complete: {folders} folders scanned.");
    return plan;
}

static List<(string Path, long Bytes, int Remaining)> CleanupFolders(CleanupPlan plan,
    HashSet<string>? deleted = null)
{
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var sizes = new Dictionary<string, (long Bytes, int Remaining)>(comparer);
    foreach (var entry in plan.Entries)
    {
        var folder = DisplayFolder(plan, entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path)!);
        while (true)
        {
            var previous = sizes.GetValueOrDefault(folder);
            var pending = deleted is null || !deleted.Contains(entry.Path);
            sizes[folder] = (previous.Bytes + (pending ? entry.Bytes : 0), previous.Remaining + (pending ? 1 : 0));
            if (comparer.Equals(folder, plan.RootPath))
                break;
            folder = Path.GetDirectoryName(folder)!;
        }
    }

    var children = sizes.Keys.Where(folder => !comparer.Equals(folder, plan.RootPath))
        .ToLookup(folder => Path.GetDirectoryName(folder)!, comparer);
    var rows = new List<(string Path, long Bytes, int Remaining)>();
    if (plan.RepositoryRoots.Contains(plan.RootPath, comparer))
        AddFolder(plan.RootPath);
    else
        foreach (var child in children[plan.RootPath].Order(comparer))
            AddFolder(child);
    return rows;

    void AddFolder(string folder)
    {
        rows.Add((folder, sizes[folder].Bytes, sizes[folder].Remaining));
        foreach (var child in children[folder].Order(comparer))
            AddFolder(child);
    }
}

static void PrintCleanupFolders(CleanupPlan plan, bool highlight)
{
    foreach (var row in CleanupFolders(plan))
    {
        var label = FormatFolder(plan.RootPath, row.Path);
        if (highlight)
            AnsiConsole.MarkupLine($"{Markup.Escape(label)}  [bold yellow]{FormatSize(row.Bytes)}[/]");
        else
            Console.WriteLine($"{label}  {FormatSize(row.Bytes)}");
    }
}

static CleanupResult PickFolders(ArtifactCleaner cleaner, CleanupPlan plan)
{
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var deleted = new HashSet<string>(comparer);
    var rows = CleanupFolders(plan, deleted);
    var saved = rows.ToDictionary(row => row.Path, _ => 0L, comparer);
    var errors = new List<string>();
    var files = 0;
    var directories = 0;
    long bytes = 0;
    var selected = 0;
    var confirming = false;
    var deleting = false;
    var finished = false;
    var status = "Cleanup removes previewed artifacts, including descendants; repository roots are preserved.";

    Spectre.Console.Rendering.IRenderable Frame()
    {
        var lines = new List<Spectre.Console.Rendering.IRenderable>
        {
            new Text("Folders to clean (sizes include all descendants):"), new Text("")
        };
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var label = Markup.Escape(FormatFolder(plan.RootPath, row.Path));
            var marker = i == selected && !finished ? "> " : "  ";
            var nameStyle = row.Remaining == 0 ? "red"
                : i == selected && confirming ? "orange1" : i == selected ? "bold reverse" : "default";
            var detail = row.Remaining == 0
                ? $"[cyan]{FormatSize(saved[row.Path])} saved[/]"
                : $"[bold yellow]{FormatSize(row.Bytes)}[/]"
                    + (saved[row.Path] > 0 ? $"  [cyan]{FormatSize(saved[row.Path])} saved[/]" : "");
            if (i == selected && confirming)
                detail += "  [orange1]Press Enter to confirm cleanup[/]";
            lines.Add(new Markup($"{marker}[{nameStyle}]{label}[/]  {detail}"));
        }
        lines.Add(new Text(""));
        lines.Add(new Markup($"Disk space saved: [bold cyan]{FormatSize(bytes)}[/]."));
        lines.Add(new Text($"Deleted {files} files and {directories} directories this session."));
        lines.Add(new Text(status));
        if (!finished && !deleting)
        {
            lines.Add(new Text(""));
            lines.Add(new Text(confirming
                ? "Enter confirms cleanup of the selected folder and descendants; Up/Down or Esc cancels confirmation."
                : "Up/Down navigate; press Delete to clean, Enter to confirm; Esc exits."));
        }
        return new Rows(lines);
    }

    // Render the whole list for now; a scrolling viewport is a separate change.
    AnsiConsole.Live(Frame()).AutoClear(false).Overflow(VerticalOverflow.Visible).Start(context =>
    {
        void Refresh()
        {
            context.UpdateTarget(Frame());
            context.Refresh();
        }

        Refresh();
        while (true)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Escape)
            {
                if (!confirming)
                    break;
                confirming = false;
            }
            else if (key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                confirming = false;
                selected = Math.Clamp(selected + (key == ConsoleKey.UpArrow ? -1 : 1), 0, rows.Count - 1);
            }
            else if (key == ConsoleKey.Delete && rows[selected].Remaining > 0)
                confirming = true;
            else if (key == ConsoleKey.Enter && confirming)
            {
                confirming = false;
                deleting = true;
                status = "Deleting selected artifacts; rechecking Git ignore rules...";
                Refresh();
                var removed = 0;
                var result = cleaner.Delete(plan.ForFolder(rows[selected].Path), onDeleted: entry =>
                {
                    deleted.Add(entry.Path);
                    foreach (var row in rows)
                        if (IsAtOrBelow(entry.Path, row.Path))
                            saved[row.Path] += entry.Bytes;
                    if (++removed % 128 == 0)
                    {
                        status = $"Deleting selected artifacts: {removed} items deleted...";
                        Refresh();
                    }
                });
                files += result.Files;
                directories += result.Directories;
                bytes += result.BytesDeleted;
                deleting = false;
                errors.AddRange(result.Errors);
                rows = CleanupFolders(plan, deleted);
                status = (rows.Any(row => row.Remaining > 0)
                    ? "Remaining items can be cleaned; failed or changed items are preserved and can be retried."
                    : "Cleanup complete. Press Esc to exit.")
                    + (errors.Count > 0 ? $" {errors.Count} session errors (reported below)." : "");
            }
            else
                continue;
            Refresh();
        }
        finished = true;
        Refresh();
    });

    Console.WriteLine();
    foreach (var error in errors)
        Console.Error.WriteLine(error);
    return new CleanupResult(files, directories, bytes, errors.AsReadOnly());
}

static string[] FolderParts(string root, string folder) => Path.GetRelativePath(root, folder)
    .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

static int FolderDepth(string root, string folder) =>
    Path.GetRelativePath(root, folder) == "." ? 0 : FolderParts(root, folder).Length;

static bool IsAtOrBelow(string path, string parent)
{
    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    return string.Equals(path, parent, comparison)
        || path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            comparison);
}

static string DisplayFolder(CleanupPlan plan, string folder) =>
    plan.RepositoryRoots.FirstOrDefault(repository => IsAtOrBelow(folder, repository)) ?? folder;

static string FormatFolder(string root, string folder) =>
    new string(' ', Math.Max(0, FolderDepth(root, folder) - 1) * 2) + new DirectoryInfo(folder).Name;

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
