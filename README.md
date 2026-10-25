# Buildup

A small .NET 10 command-line utility that recursively finds Git working trees under
the current directory and deletes their ignored files and empty artifact directories.
Git must be installed and available on `PATH`.

```powershell
dotnet build Buildup.slnx
```

From the directory you want to clean, run the built executable using its absolute path:

```powershell
C:\path\to\buildup\Build\bin\Buildup.Cli\Debug\net10.0\buildup.exe
```

For distribution, publish a self-contained single executable:

```powershell
dotnet publish SRC/Buildup.Cli/Buildup.Cli.csproj -c Release -r win-x64
```

The result is `Build/publish/win-x64/buildup.exe`. Copy that executable alone; the
target machine does not need .NET or companion DLLs. Git must still be on `PATH`.
The bundled native runtime extracts to the OS temporary directory when needed.

There is no single self-contained executable that runs on every OS and architecture.
Publish separately with `-r linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, or
`win-arm64` as needed. Each goes into its own `Build/publish/<runtime>/` directory;
Linux/macOS executables are named `buildup` without `.exe`.

`--help` prints usage. Unknown arguments return exit code 2;
cleanup errors return 1 and are printed to standard error. Successful runs return 0.
Interactive terminals show a Spectre.Console spinner while scanning, including
the current folder and a folder count. After confirmation, a progress bar tracks
successfully deleted files and directories against the preview's item count.
Skipped or failed items do not advance the bar. Animation is cleared before the
final result; errors and disk space totals are still reported normally.
After folder discovery, the spinner switches to repository validation and then
`Calculating results`, with repository counts. These phases query Git and build
the cleanup plan, including file sizes; they can take time even after the last
folder has been discovered. A second, gray line shows the current repository and
activity (querying Git, measuring ignored files, or checking artifact folders),
with eligible file/folder counts and bytes accumulated across the scan. During
file and folder inspection it also shows the current path. File/folder detail
updates are limited to ten per second; activity changes appear immediately.
Plain output announces each phase once and prints
repository details, with additional updates at most once per second. Skipped
repositories use a yellow `SKIP` label and gray details in interactive ANSI
terminals; redirected output keeps a plain `SKIP` label. Blank lines separate the
scan summary, skipped repositories, cleanup tree, size estimate, and confirmation.
Redirected output and terminals without interactive ANSI support use plain lines:
folders are printed as discovery enters them, stopping display at each repository root.
Folders are shown by name only, without trailing separators, with two spaces of
indentation per nested level relative to the directory where the command was run.
The launch directory is omitted from the tree unless it is itself a repository.
There is no depth limit: intermediate folders are shown until the first Git
repository on each path, and repository contents (such as `bin`, `obj`, and nested
repositories) are not expanded in the preview. Scanning and cleanup still include
every level, and the full cleanup plan retains every eligible path. Only branches
containing cleanup candidates appear in the preview.
Each row shows the total size of eligible ignored files in that folder and all its
descendants, including contents hidden beneath repository roots. Parent totals include
their child totals; do not add the rows together. Sizes appear in bold yellow in
interactive ANSI terminals. Folders containing individually ignored files are
included; only eligible ignored contents are removed from those folders.
The deletion prompt accepts `Y` to delete all previewed artifacts, `N` to cancel,
or `P` to pick folders (case-insensitive, followed by Enter). Empty input, Escape,
unrecognized input, or end of input cancels deletion. Nothing is deleted during scanning.
In pick mode, every displayed folder receives a stable number. Type a number and
press Enter to delete that folder's previewed artifacts, including all descendants.
The folder itself is removed only if it is an eligible empty artifact directory;
repository roots and preserved files remain. Parent selections include their
children; selecting a repository includes its previewed artifacts and nested repositories.
`Y` deletes all remaining previewed artifacts, and `N`, Enter, Escape, or end of
input finishes picking. A multiline `Choose how to proceed` prompt lists these
options and the available folder numbers. Interactive ANSI terminals redraw short
lists in place and mark completed folders red with `deleted`. Lists too tall for
the terminal print in full so every number is available in scrollback; redirected
output prints refreshed plain lists. Disk space saved and deleted file/directory
counts accumulate across the entire picking session. Saved space appears in cyan
and deletion status in gray in interactive ANSI terminals.
Sizes update to the remaining previewed file sizes. Only successful
deletions mark a folder complete, so failed or changed items stay unmarked and
can be retried. Git rules are rechecked for every selection, and newly created
files are never added to the original preview.
With plain output, each affected folder is grouped at its repository boundary and
printed once during deletion with a `Removing:` prefix.
File sizes are totaled during scanning, and `Estimated disk space savings:` is
printed before confirmation. Sizes are read again immediately before deletion and
counted only after successful deletion. A final line reports `Disk space saved:`
in B, KiB, MiB, or larger units. These totals use file lengths; actual freed disk
space can differ due to filesystem allocation, compression, or hard links.
There is no `--dry-run` option; every run previews the cleanup before confirmation.

**Confirming deletion permanently deletes ignored files, regardless of age.**
This includes ignored secrets, local databases, IDE settings, and dependencies if your
ignore rules cover them. Git's standard ignore rules apply: `.gitignore` files,
`.git/info/exclude`, and global Git excludes, including negation rules. Tracked files
are preserved even when they match ignore patterns.

Nested repositories, worktrees, and initialized submodules are handled separately.
Git metadata, bare repositories, symbolic links, and junctions are left alone.
Only repository roots found at or below the current directory are eligible. Running
in Downloads, Photos, or any folder without repositories deletes nothing, even if it
contains `.gitignore` files or is inside an ancestor repository. To clean a project,
run from its repository root or a parent folder containing it. Running from an
ordinary project subdirectory only cleans repositories nested beneath that directory.
Each discovered `.git` marker is validated with Git before any cleanup starts.
Folders that Git rejects (including invalid repositories or repositories with dubious
ownership) are skipped without failing the run. Their paths and Git's reason are
printed before the deletion prompt, and their contents are protected from cleanup.
Git environment overrides (`GIT_*`) are ignored so they cannot redirect cleanup to a
different worktree or index; on-disk ignore rules and Git configuration still apply.
Empty ignored directories are also removed. Ignore rules and tracked status are
rechecked after confirmation, and deletion is limited to paths in the preview.
Directories are removed only when empty. Avoid running builds or changing ignore
rules during cleanup.

`SRC/Buildup.Core` owns discovery and deletion; `SRC/Buildup.Cli` only handles arguments
and output. `Directory.Build.props` redirects build and intermediate output into
`Build/`; `Directory.Packages.props` enables central package management. The CLI uses
Spectre.Console for terminal animations; the core has no external package dependencies.
