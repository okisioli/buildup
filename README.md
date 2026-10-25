# Buildup

A small .NET 10 command-line utility that recursively finds Git working trees under
the current directory and deletes their ignored files and empty artifact directories.
When run inside a repository, it cleans that entire repository instead, locating
the nearest containing repository when launched from a subfolder.
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
the current folder and a folder count. During cleanup, the folder list shows
progress and cumulative deleted file/directory counts and disk space saved.
After discovery, validation and calculation keep the spinner and repository counter,
with an ASCII progress bar below them. The bar counts completed repositories;
the counter identifies the repository currently being processed. The current folder
appears in gray on its own line, followed by the operation and eligible item/byte
totals on separate lines. Detail updates are limited to ten per second; activity
changes appear immediately. Scan rendering and styles live in
`SRC/Buildup.Cli/ScanProgressColumn.cs`; scan callbacks are wired in `Program.cs`.
Plain output announces each phase once and prints
repository details, with additional updates at most once per second. Skipped
repositories use a yellow `SKIP` label and gray details in interactive ANSI
terminals; redirected output keeps a plain `SKIP` label. Blank lines separate the
scan summary, skipped repositories, cleanup tree, size estimate, and confirmation.
Redirected output and terminals without interactive ANSI support use plain lines:
folders are printed as discovery enters them, stopping display at each repository root.
Folders are shown by name only, without trailing separators, with two spaces of
indentation per nested level relative to the directory where the command was run.
The launch directory is omitted from the tree.
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
When launched inside a repository, the app identifies that repository by its full
path and displays one cleanup row. Otherwise, the picker shows the folder hierarchy.
Use Up/Down to select a row, Delete to prepare cleanup, and Enter to confirm.
Moving Up/Down or pressing Esc cancels a pending confirmation; Esc otherwise exits.
Pending rows turn orange; completed folder names turn red with the actual saved
amount in cyan. Cleanup includes descendants but preserves repository roots and
noneligible files. Parent saved amounts include their children; do not add rows together.
Failed or changed items remain available for retry. Git rules are rechecked for
each selection, and newly created files are never added to the original preview.
The picker redraws the whole list without a scrolling viewport or sticky instructions;
rows may be hidden when the list exceeds the terminal height. Redirected input/output
and terminals without interactive ANSI support print the preview without deleting.
Nothing is deleted during scanning.
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
The app checks the current directory and then each parent up to the filesystem
root for the nearest `.git` directory or file (including worktrees). If found,
that directory becomes the scan root, including its nested repositories; sibling
repositories outside it are not included. Upward lookup stops at symbolic links
or junctions and never bypasses a rejected `.git` marker to choose a higher repository.
If no containing repository is found, only repositories at or below the launch
directory are eligible. A folder with no containing or descendant repositories
deletes nothing, even if it contains `.gitignore` files.
Each discovered `.git` marker is validated with Git before any cleanup starts.
Folders that Git rejects (including invalid repositories or repositories with dubious
ownership) are skipped without failing the run. Their paths and Git's reason are
printed before the cleanup preview, and their contents are protected from cleanup.
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
