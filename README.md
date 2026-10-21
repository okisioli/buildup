# Buildup

A small .NET 10 command-line utility that recursively finds Git working trees under
the current directory and deletes their ignored files and empty artifact directories.
Git must be installed and available on `PATH`.

```powershell
dotnet build Buildup.slnx
```

From the directory you want to clean, run the built executable using its absolute path:

```powershell
C:\path\to\buildup\Build\bin\Buildup.Cli\Debug\net10.0\buildup.exe --dry-run
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
Each deleted path (or proposed deletion with `--dry-run`) is printed without formatting.

**Running without `--dry-run` permanently deletes ignored files, regardless of age.**
This includes ignored secrets, local databases, IDE settings, and dependencies if your
ignore rules cover them. Git's standard ignore rules apply: `.gitignore` files,
`.git/info/exclude`, and global Git excludes, including negation rules. Tracked files
are preserved even when they match ignore patterns.

Nested repositories, worktrees, and initialized submodules are handled separately.
Git metadata, bare repositories, symbolic links, and junctions are left alone.
Files outside repositories are left alone. When started inside a working tree, only
paths beneath the current directory are eligible. Empty ignored directories are also
removed. Avoid running builds or changing ignore rules during cleanup.

`SRC/Buildup.Core` owns discovery and deletion; `SRC/Buildup.Cli` only handles arguments
and output. `Directory.Build.props` redirects build and intermediate output into
`Build/`; `Directory.Packages.props` enables central package management. There are no
external package dependencies.
