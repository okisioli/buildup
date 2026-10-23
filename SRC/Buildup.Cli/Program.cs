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
    Console.Write("Delete these folders? [Y] all / [N] cancel / [P] pick (Enter/Esc cancels): ");
    var choice = ReadChoice();
    if (string.Equals(choice, "p", StringComparison.OrdinalIgnoreCase))
    {
        var picked = PickFolders(cleaner, plan, animate);
        PrintSavedSpace(picked.BytesDeleted, animate);
        return plan.Errors.Count == 0 && picked.Errors.Count == 0 ? 0 : 1;
    }
    if (!string.Equals(choice, "y", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Deletion cancelled.");
        return plan.Errors.Count == 0 ? 0 : 1;
    }

    var result = Delete(cleaner, plan, animate);
    foreach (var error in result.Errors)
        Console.Error.WriteLine(error);
    PrintSavedSpace(result.BytesDeleted, animate);
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
        var detailTimer = System.Diagnostics.Stopwatch.StartNew();
        var firstDetail = true;
        var plainPlan = cleaner.Scan(root, folder =>
        {
            folders++;
            if (FolderDepth(root, folder) <= 2)
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
            context.Status($"Scanning ({folders} folders): {Markup.Escape(Path.GetRelativePath(root, DisplayFolder(root, folder)))}");
        }, (phase, current, total) =>
        {
            phaseStatus = $"{phase} ({current}/{total})...";
            context.Status(phaseStatus);
        }, detail => context.Status($"{phaseStatus}\n  [grey]{Markup.Escape(detail)}[/]")));
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

static List<(string Path, long Bytes, int Remaining)> CleanupFolders(CleanupPlan plan,
    HashSet<string>? deleted = null)
{
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var sizes = new Dictionary<string, (long Bytes, int Remaining)>(comparer);
    foreach (var entry in plan.Entries)
    {
        var folder = DisplayFolder(plan.RootPath, entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path)!);
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
    AddFolder(plan.RootPath);
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

static string? ReadChoice(Action<string>? onInput = null)
{
    if (Console.IsInputRedirected)
        return Console.ReadLine()?.Trim();
    var input = "";
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key is ConsoleKey.Escape or ConsoleKey.Enter)
        {
            if (onInput is null)
                Console.WriteLine();
            return key.Key == ConsoleKey.Escape ? null : input.Trim();
        }
        if (key.Key == ConsoleKey.Backspace && input.Length > 0)
        {
            input = input[..^1];
            if (onInput is null)
                Console.Write("\b \b");
        }
        else if (!char.IsControl(key.KeyChar))
        {
            input += key.KeyChar;
            if (onInput is null)
                Console.Write(key.KeyChar);
        }
        onInput?.Invoke(input);
    }
}

static CleanupResult PickFolders(ArtifactCleaner cleaner, CleanupPlan plan, bool interactive)
{
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var deleted = new HashSet<string>(comparer);
    var rows = CleanupFolders(plan, deleted);
    var errors = new List<string>();
    var files = 0;
    var directories = 0;
    long bytes = 0;
    var status = "Choosing a folder deletes its previewed artifacts, including descendants.";
    var input = "";
    var deleting = false;
    var finished = false;

    string Prompt() => "Choose how to proceed\n"
        + "- [ Y ] Delete all remaining\n"
        + "- [ N, Enter, Esc ] Abort\n"
        + $"- [ 1...{rows.Count} ] Delete this folder and its subfolders\n"
        + "Your choice: ";

    Spectre.Console.Rendering.IRenderable Frame()
    {
        var lines = new List<Spectre.Console.Rendering.IRenderable>
        {
            new Text("Pick folders to clean (numbers stay the same):"), new Text("")
        };
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var label = Markup.Escape($"{i + 1}. {FormatFolder(plan.RootPath, row.Path)}");
            lines.Add(new Markup(row.Remaining == 0
                ? $"[red]{label} — deleted[/]"
                : $"{label}  [bold yellow]{FormatSize(row.Bytes)}[/]"));
        }
        lines.Add(new Text(""));
        lines.Add(new Markup($"Disk space saved: [bold cyan]{FormatSize(bytes)}[/]."));
        lines.Add(new Text($"Deleted {files} files and {directories} directories this session."));
        lines.Add(new Text(status, deleting ? new Style(Color.Grey) : Style.Plain));
        lines.Add(new Text(""));
        if (!finished && !deleting)
            lines.Add(new Text(Prompt() + input));
        return new Rows(lines);
    }

    void Run(Action refresh, bool live)
    {
        while (rows[0].Remaining > 0)
        {
            input = "";
            refresh();
            var choice = ReadChoice(live ? value => { input = value; refresh(); } : null);
            if (string.IsNullOrEmpty(choice) || choice.Equals("n", StringComparison.OrdinalIgnoreCase))
                break;
            CleanupPlan selection;
            if (choice.Equals("y", StringComparison.OrdinalIgnoreCase))
                selection = plan;
            else if (int.TryParse(choice, out var number) && number >= 1 && number <= rows.Count)
            {
                if (rows[number - 1].Remaining == 0)
                {
                    status = "That folder's previewed artifacts have already been deleted.";
                    continue;
                }
                selection = plan.ForFolder(rows[number - 1].Path);
            }
            else
            {
                status = $"Enter a folder number from 1 to {rows.Count}, Y, or N.";
                continue;
            }
            status = "Deleting selected artifacts; rechecking Git ignore rules...";
            deleting = true;
            input = "";
            refresh();
            var removed = 0;
            var result = cleaner.Delete(selection, onDeleted: entry =>
            {
                deleted.Add(entry.Path);
                if (++removed % 128 == 0 && live)
                {
                    status = $"Deleting selected artifacts: {removed} items deleted...";
                    refresh();
                }
            });
            files += result.Files;
            directories += result.Directories;
            bytes += result.BytesDeleted;
            deleting = false;
            errors.AddRange(result.Errors);
            rows = CleanupFolders(plan, deleted);
            status = (rows[0].Remaining > 0 ? "Unmarked items remain; failed or changed items are preserved." : "Cleanup complete.")
                + (errors.Count > 0 ? $" {errors.Count} session errors (reported below)." : "");
        }
        input = "";
        finished = true;
        refresh();
    }

    // A live frame taller than the terminal can hide its first rows. Long lists use
    // ordinary output so every numbered folder remains available in scrollback.
    var width = Math.Max(1, AnsiConsole.Profile.Width);
    var listHeight = rows.Sum(row => 1 + (FormatFolder(plan.RootPath, row.Path).Length + 40) / width);
    if (interactive && !Console.IsInputRedirected && listHeight + 18 < AnsiConsole.Profile.Height)
        AnsiConsole.Live(Frame()).AutoClear(false).Overflow(VerticalOverflow.Visible).Start(context =>
            Run(() => { context.UpdateTarget(Frame()); context.Refresh(); }, true));
    else
        Run(() =>
        {
            Console.WriteLine();
            if (interactive)
            {
                AnsiConsole.Write(Frame());
                Console.WriteLine();
                return;
            }
            Console.WriteLine("Pick folders to clean (numbers stay the same):");
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                Console.WriteLine($"{i + 1}. {FormatFolder(plan.RootPath, row.Path)}  "
                    + (row.Remaining == 0 ? "— deleted" : FormatSize(row.Bytes)));
            }
            PrintSavedSpace(bytes, false);
            Console.WriteLine($"Deleted {files} files and {directories} directories this session.");
            Console.WriteLine(status);
            if (!finished && !deleting)
                Console.Write(Prompt());
        }, false);

    Console.WriteLine();
    foreach (var error in errors)
        Console.Error.WriteLine(error);
    return new CleanupResult(files, directories, bytes, errors.AsReadOnly());
}

static void PrintSavedSpace(long bytes, bool highlight)
{
    if (highlight)
        AnsiConsole.MarkupLine($"Disk space saved: [bold cyan]{FormatSize(bytes)}[/].");
    else
        Console.WriteLine($"Disk space saved: {FormatSize(bytes)}.");
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
