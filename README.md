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
Redirected output and terminals without interactive ANSI support use plain lines:
folders in the first three levels are printed as discovery enters them.
Folders are shown by name only, without trailing separators, with two spaces of
indentation per nested level relative to the directory where the command was run.
The current directory is shown by name and counts as the first level. Displayed
folder trees stop at its grandchildren; scanning and cleanup still include every
level, and the full cleanup plan retains every eligible path. After scanning,
cleanup candidates are grouped into this three-level tree without individual files.
Each row shows the total size of eligible ignored files in that folder and all its
descendants, including folders hidden by the display limit. Parent totals include
their child totals; do not add the rows together. Sizes appear in bold yellow in
interactive ANSI terminals. Folders containing individually ignored files are
included; only eligible ignored contents are removed from those folders.
The prompt `Delete these folders? (y/n)` requires `y` (case-insensitive) to proceed.
Any other response or end of input cancels deletion. Nothing is deleted during scanning.
With plain output, each affected folder is grouped at the same display depth and
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
