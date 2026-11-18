using Okisioli.Tools.Buildup.Core;
using Spectre.Console;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Usage: buildup");
    Console.WriteLine("Deletes ignored files in the current or nearest containing Git repository, or repositories below the current directory.");
    Console.WriteLine("Previews cleanup; use Up/Down to select, Right/+ to expand, Left/- to collapse the folder or its parent, Delete to clean, and Enter to confirm.");
    return 0;
}

if (args.Length > 0)
{
    Console.Error.WriteLine("Usage: buildup");
    return 2;
}

var interactiveScreen = !Console.IsOutputRedirected && !Console.IsInputRedirected
    && AnsiConsole.Profile.Capabilities.Ansi && AnsiConsole.Profile.Capabilities.Interactive;
CleanupPlan? finalPlan = null;
var finalResult = new CleanupResult(0, 0, 0, Array.Empty<string>());
var exitErrors = new List<string>();
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    // Unwind live rendering before restoring the original terminal buffer.
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancelHandler;
var previousTreatControlCAsInput = Console.TreatControlCAsInput;
if (interactiveScreen)
    Console.TreatControlCAsInput = true;
if (interactiveScreen)
    Console.Write("\u001b[?1049h");
try
{
    var cleaner = new ArtifactCleaner();
    var currentDirectory = Directory.GetCurrentDirectory();
    var root = FindContainingRepository(currentDirectory) ?? currentDirectory;
    var animate = interactiveScreen;
    var plan = Scan(cleaner, root, animate, cancellation.Token);
    cancellation.Token.ThrowIfCancellationRequested();
    finalPlan = plan;
    if (plan.SkippedRepositories.Count > 0)
        Console.WriteLine();
    foreach (var skipped in plan.SkippedRepositories)
    {
        if (animate)
        {
            AnsiConsole.Write(new Paragraph("SKIP", Constants.SkippedLabel)
                .Append(" ").Append(skipped, Constants.SkippedDetail));
            AnsiConsole.WriteLine();
        }
        else
            Console.WriteLine($"SKIP {skipped}");
    }
    Console.WriteLine();
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var singleRepository = plan.RepositoryRoots.Contains(plan.RootPath, comparer);
    if (singleRepository)
        Console.WriteLine(comparer.Equals(root, currentDirectory)
            ? $"This is a Git repository: {root}"
            : $"Using containing Git repository: {root}");
    if (animate && plan.RepositoryRoots.Count > 0)
    {
        finalResult = PickFolders(cleaner, plan, cancellation.Token);
        return finalResult.Errors.Count == 0 ? 0 : 1;
    }
    if (plan.Entries.Count == 0)
    {
        Console.WriteLine("No folders to clean.");
        return plan.Errors.Count == 0 ? 0 : 1;
    }

    Console.WriteLine($"Estimated disk space savings: {FormatSize(plan.BytesToDelete)}.");
    Console.WriteLine();
    PrintCleanupFolders(plan, false);
    Console.WriteLine("Interactive terminal required for keyboard cleanup. Nothing deleted.");
    return plan.Errors.Count == 0 ? 0 : 1;
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
    or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
{
    exitErrors.Add(ex.Message);
    return 1;
}
finally
{
    if (interactiveScreen)
        Console.Write("\u001b[?1049l");
    Console.WriteLine();
    if (finalPlan is not null)
    {
        AnsiConsole.Write(new Paragraph("Disk space saved: ")
            .Append(FormatSize(finalResult.BytesDeleted), Constants.TotalSavedSize).Append("."));
        AnsiConsole.WriteLine();
        Console.WriteLine($"Deleted {finalResult.Files} files and {finalResult.Directories} directories.");

        var errors = finalPlan.Errors.Concat(finalResult.Errors);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var groups = errors.GroupBy(error => finalPlan.RepositoryRoots
            .FirstOrDefault(repository => error.StartsWith(repository + Path.DirectorySeparatorChar, comparison)
                || error.StartsWith(repository + ":", comparison)) ?? finalPlan.RootPath, comparer);
        foreach (var group in groups)
        {
            var folder = Path.GetRelativePath(finalPlan.RootPath, group.Key);
            if (folder == ".")
                folder = new DirectoryInfo(group.Key).Name;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Text($"Leftover files in '{folder.Replace('\\', '/')}'", Constants.CleanupErrorHeading));
            AnsiConsole.WriteLine();
            foreach (var error in group.Distinct(comparer))
            {
                AnsiConsole.Write(new Text($"  {error}", Constants.CleanupErrorDetail));
                AnsiConsole.WriteLine();
            }
        }
    }
    foreach (var error in exitErrors)
        Console.Error.WriteLine(error);
    if (finalPlan is not null || exitErrors.Count > 0)
        Console.WriteLine();
    if (interactiveScreen)
        Console.TreatControlCAsInput = previousTreatControlCAsInput;
    Console.CancelKeyPress -= cancelHandler;
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

static CleanupPlan Scan(ArtifactCleaner cleaner, string root, bool animate, CancellationToken cancellationToken)
{
    var folders = 0;

    void CheckCancellation()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (animate && Console.KeyAvailable)
        {
            var input = Console.ReadKey(intercept: true);
            if (input.Key == ConsoleKey.C && input.Modifiers.HasFlag(ConsoleModifiers.Control))
                throw new OperationCanceledException();
        }
    }

    if (!animate)
    {
        string? lastPhase = null;
        var detailTimer = System.Diagnostics.Stopwatch.StartNew();
        var firstDetail = true;
        var plainPlan = cleaner.Scan(root, folder =>
        {
            CheckCancellation();
            folders++;
            if (folder != root)
                Console.WriteLine($"Scanning ({folders} folders):\n  {FormatFolder(root, folder)}");
        }, (phase, current, total) =>
        {
            CheckCancellation();
            firstDetail = true;
            if (phase != lastPhase)
            {
                Console.WriteLine($"{phase} ({current}/{total})...");
                lastPhase = phase;
            }
        }, detail =>
        {
            CheckCancellation();
            if (!firstDetail && detailTimer.ElapsedMilliseconds < 1000)
                return;
            Console.WriteLine($"  {detail}");
            firstDetail = false;
            detailTimer.Restart();
        });
        Console.WriteLine($"Scan complete: {folders} folders scanned.");
        return plainPlan;
    }

    var column = new ScanProgressColumn();
    var plan = AnsiConsole.Progress().AutoClear(true).Columns(column).Start(context =>
    {
        var task = context.AddTask("Scanning for Git repositories...");
        task.IsIndeterminate = true;
        string? lastPhase = null;
        var result = cleaner.Prepare(root, folder =>
        {
            CheckCancellation();
            folders++;
            task.Description = $"Scanning ({folders} folders):";
            column.Detail = Path.GetRelativePath(root, folder);
            if (column.Detail == ".")
                column.Detail = new DirectoryInfo(root).Name;
            context.Refresh();
        }, (phase, current, total) =>
        {
            CheckCancellation();
            // Counters identify the active repository; the bar counts completed ones.
            if (phase != lastPhase)
            {
                task.Value = 0;
                task.MaxValue = Math.Max(1, total);
                lastPhase = phase;
            }
            task.IsIndeterminate = false;
            task.Value = Math.Max(0, current - 1);
            task.Description = $"{phase} ({current}/{total})...";
            column.Detail = "";
            context.Refresh();
        }, detail =>
        {
            CheckCancellation();
            column.Detail = detail;
            context.Refresh();
        });
        task.Value = task.MaxValue;
        task.StopTask();
        context.Refresh();
        return result;
    });
    Console.WriteLine($"Scan complete: {folders} folders scanned.");
    return plan;
}

static List<(string Path, long Bytes, int Remaining, bool HasChildren)> CleanupFolders(CleanupPlan plan,
    HashSet<string>? deleted = null, IEnumerable<string>? pendingRepositories = null)
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

    // Pending branches are visible immediately; measured entries retain eligible
    // branches (including completed cleanup results) after pending placeholders disappear.
    foreach (var repository in pendingRepositories ?? Array.Empty<string>())
    {
        var folder = repository;
        while (true)
        {
            sizes.TryAdd(folder, (0, 0));
            if (comparer.Equals(folder, plan.RootPath))
                break;
            folder = Path.GetDirectoryName(folder)!;
        }
    }

    var children = sizes.Keys.Where(folder => !comparer.Equals(folder, plan.RootPath))
        .ToLookup(folder => Path.GetDirectoryName(folder)!, comparer);
    var rows = new List<(string Path, long Bytes, int Remaining, bool HasChildren)>();
    if (plan.RepositoryRoots.Contains(plan.RootPath, comparer) && sizes.ContainsKey(plan.RootPath))
        AddFolder(plan.RootPath);
    else
        foreach (var child in children[plan.RootPath].Order(comparer))
            AddFolder(child);
    return rows;

    void AddFolder(string folder)
    {
        rows.Add((folder, sizes[folder].Bytes, sizes[folder].Remaining, children.Contains(folder)));
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
        {
            AnsiConsole.Write(new Paragraph(label, Constants.FolderName)
                .Append("  ").Append(FormatSize(row.Bytes), Constants.FolderSize));
            AnsiConsole.WriteLine();
        }
        else
            Console.WriteLine($"{label}  {FormatSize(row.Bytes)}");
    }
}

static CleanupResult PickFolders(ArtifactCleaner cleaner, CleanupPlan plan, CancellationToken cancellationToken)
{
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    CleanupCalculation? latest = null;
    var completed = new HashSet<string>(comparer);
    var prepared = plan;
    var calculation = Task.Run(() => cleaner.Calculate(prepared,
        onUpdate: update => Interlocked.Exchange(ref latest, update),
        cancellationToken: cancellation.Token));
    var animation = System.Diagnostics.Stopwatch.StartNew();
    var waiting = false;
    var calculationFinished = false;
    bool IsCalculating(string folder) => plan.RepositoryRoots
        .Any(repository => IsAtOrBelow(repository, folder) && !completed.Contains(repository));

    var deleted = new HashSet<string>(comparer);
    var expanded = new HashSet<string>(comparer);
    var allRows = CleanupFolders(plan, deleted, plan.RepositoryRoots);
    var rows = VisibleRows();
    var saved = allRows.ToDictionary(row => row.Path, _ => 0L, comparer);
    var errors = new List<string>();
    var attemptErrors = new List<string>();
    var files = 0;
    var directories = 0;
    long bytes = 0;
    var selected = 0;
    var confirming = false;
    var deleting = false;
    var status = "Calculating cleanup sizes; empty branches are removed as counting finishes.";

    void RebuildRows()
    {
        var selectedPath = rows.Count > 0 ? rows[selected].Path : null;
        allRows = CleanupFolders(plan, deleted,
            plan.RepositoryRoots.Where(repository => !completed.Contains(repository)));
        rows = VisibleRows();
        var newSelection = selectedPath is null ? -1
            : rows.FindIndex(row => comparer.Equals(row.Path, selectedPath));
        if (newSelection < 0)
            confirming = waiting = false;
        selected = newSelection >= 0 ? newSelection : Math.Clamp(selected, 0, Math.Max(0, rows.Count - 1));
        foreach (var row in allRows)
            saved.TryAdd(row.Path, 0);
    }

    List<(string Path, long Bytes, int Remaining, bool HasChildren)> VisibleRows()
    {
        var visible = new List<(string Path, long Bytes, int Remaining, bool HasChildren)>();
        string? collapsedFolder = null;
        // CleanupFolders returns parents before their contiguous descendants.
        foreach (var row in allRows)
        {
            if (collapsedFolder is not null && IsAtOrBelow(row.Path, collapsedFolder))
                continue;
            visible.Add(row);
            collapsedFolder = row.HasChildren && !expanded.Contains(row.Path) ? row.Path : null;
        }
        return visible;
    }

    Spectre.Console.Rendering.IRenderable Frame()
    {
        var lines = new List<Spectre.Console.Rendering.IRenderable>
        {
            new Text("Folders to clean (sizes include all descendants):"), new Text("")
        };
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var label = FormatFolder(plan.RootPath, row.Path);
            var isSelected = i == selected;
            var calculating = IsCalculating(row.Path);
            var marker = calculating ? $"{Spinner.Known.Line.Frames[(int)(animation.ElapsedMilliseconds / 100 % Spinner.Known.Line.Frames.Count)]} "
                : row.HasChildren && (i == selected || expanded.Contains(row.Path)) ? "> " : "  ";
            var pendingCleanup = confirming && IsAtOrBelow(row.Path, rows[selected].Path);
            var nameStyle = pendingCleanup
                ? isSelected ? Constants.ConfirmationFolder : Constants.ConfirmationDescendant
                : calculating ? (isSelected ? Constants.SelectedCalculatingFolder : Constants.CalculatingFolder)
                : row.Remaining == 0 ? (isSelected ? Constants.SelectedCompletedFolder : Constants.CompletedFolder)
                : isSelected ? Constants.SelectedFolder : Constants.FolderName;
            var sizeStyle = pendingCleanup ? Constants.ConfirmationSize : Constants.FolderSize;
            var line = new Paragraph(marker, calculating ? Constants.CalculatingSpinner : Style.Plain)
                .Append(label, nameStyle).Append("  ");
            if (row.Remaining == 0 && !calculating)
                line.Append($"{FormatSize(saved.GetValueOrDefault(row.Path))} saved", Constants.SavedSize);
            else
            {
                line.Append(FormatSize(row.Bytes), sizeStyle);
                if (saved.GetValueOrDefault(row.Path) > 0)
                    line.Append("  ").Append($"{FormatSize(saved[row.Path])} saved", Constants.SavedSize);
            }
            if (i == selected && confirming)
                line.Append("  ").Append(waiting ? "Still calculating, please wait" : "Press Enter to confirm cleanup",
                    Constants.ConfirmationMessage);
            lines.Add(line);
        }
        lines.Add(new Text(""));
        lines.Add(new Paragraph("Disk space saved: ")
            .Append(FormatSize(bytes), Constants.TotalSavedSize).Append("."));
        lines.Add(new Text($"Deleted {files} files and {directories} directories."));
        lines.Add(new Text(status));
        foreach (var error in attemptErrors)
            lines.Add(new Text(error, Constants.CleanupErrorHeading));
        if (!deleting)
        {
            lines.Add(new Text(""));
            lines.Add(new Text(confirming && waiting
                ? "Still calculating, please wait; arrow keys, +/- or Esc cancel confirmation."
                : confirming ? "Enter confirms cleanup of the selected folder and descendants; arrow keys, +/- or Esc cancel confirmation."
                : "Up/Down navigate; Right/+ expand; Left/- collapse folder or parent; Delete cleans; Enter confirms; Esc exits."));
        }
        return new Rows(lines);
    }

    try
    {
        // Render the whole list for now; a scrolling viewport is a separate change.
        AnsiConsole.Live(Frame()).AutoClear(true).Overflow(VerticalOverflow.Visible).Start(context =>
        {
            void Refresh()
            {
                context.UpdateTarget(Frame());
                context.Refresh();
            }

            Refresh();
            while (!cancellationToken.IsCancellationRequested)
            {
                var update = Interlocked.Exchange(ref latest, null);
                if (update is not null && !calculationFinished)
                {
                    plan = update.Plan;
                    completed = new HashSet<string>(update.CompletedRepositories, comparer);
                    RebuildRows();
                }
                if (!calculationFinished && calculation.IsCompleted)
                {
                    plan = calculation.GetAwaiter().GetResult();
                    completed.UnionWith(plan.RepositoryRoots);
                    errors.AddRange(plan.Errors);
                    calculationFinished = true;
                    RebuildRows();
                    status = plan.Entries.Count == 0 ? "No folders to clean. Press Esc to exit."
                        : "Cleanup removes previewed artifacts, including descendants; repository roots are preserved.";
                }
                if (confirming && waiting && !IsCalculating(rows[selected].Path))
                    confirming = waiting = false;
                Refresh();
                if (!Console.KeyAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }
                var input = Console.ReadKey(intercept: true);
                var key = input.Key;
                if (key == ConsoleKey.C && input.Modifiers.HasFlag(ConsoleModifiers.Control))
                    break;
                if (key == ConsoleKey.Escape)
                {
                    if (!confirming)
                        break;
                    confirming = false;
                }
                else if (rows.Count == 0)
                    continue;
                else if (key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
                {
                    confirming = false;
                    selected = Math.Clamp(selected + (key == ConsoleKey.UpArrow ? -1 : 1), 0, rows.Count - 1);
                }
                else if (key is ConsoleKey.RightArrow or ConsoleKey.LeftArrow || input.KeyChar is '+' or '-')
                {
                    confirming = false;
                    var row = rows[selected];
                    if (key == ConsoleKey.RightArrow || input.KeyChar == '+')
                    {
                        if (row.HasChildren)
                            expanded.Add(row.Path);
                    }
                    else if (!expanded.Remove(row.Path) && Path.GetDirectoryName(row.Path) is { } parent
                        && expanded.Remove(parent))
                        selected = rows.FindIndex(candidate => comparer.Equals(candidate.Path, parent));

                    rows = VisibleRows();
                }
                else if (key == ConsoleKey.Delete && (IsCalculating(rows[selected].Path) || rows[selected].Remaining > 0))
                {
                    confirming = true;
                    waiting = IsCalculating(rows[selected].Path);
                }
                else if (key == ConsoleKey.Enter && confirming && !waiting && !IsCalculating(rows[selected].Path))
                {
                    confirming = false;
                    deleting = true;
                    status = "Deleting selected artifacts; rechecking Git ignore rules...";
                    Refresh();
                    var removed = 0;
                    var result = cleaner.Delete(plan.ForFolder(rows[selected].Path), onDeleted: entry =>
                    {
                        deleted.Add(entry.Path);
                        foreach (var row in allRows)
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
                    if (result.Errors.Count > 0)
                        attemptErrors.Add($"ERROR: {result.Errors.Count} files couldn't be deleted");
                    RebuildRows();
                    status = allRows.Any(row => row.Remaining > 0 || IsCalculating(row.Path))
                        ? "Remaining items can be cleaned; failed or changed items are preserved and can be retried."
                        : "Cleanup complete. Press Esc to exit.";
                }
                else
                    continue;
                Refresh();
            }
        });
    }
    finally
    {
        cancellation.Cancel();
        try
        {
            calculation.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
    if (!calculationFinished)
        errors.AddRange(plan.Errors);
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
