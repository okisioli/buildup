# Buildup

Interactive cleanup of Git-ignored files, with a preview before permanent deletion.

## Install and run

Requires the .NET 10 SDK and Git on PATH.

```powershell
dotnet tool install --global Okisioli.Tools.Buildup --version 1.0.0
buildup
```

Run from the folder you want to scan. Use Up/Down to select, Right/+ to expand,
Left/- to collapse, Delete to clean, and Enter to confirm. Use `buildup --help`
for usage. Ensure the .NET tools directory is on PATH if the command is not found.

## Development

```powershell
dotnet build Okisioli.Tools.Buildup.slnx -m:1
dotnet run --project SRC/Okisioli.Tools.Buildup.CLI
dotnet pack SRC/Okisioli.Tools.Buildup.CLI -c Release -o Build/packages -m:1
```

There is currently no test project. To verify the package locally:

```powershell
dotnet tool install Okisioli.Tools.Buildup --version 1.0.0 --add-source ./Build/packages --tool-path ./Build/tool
./Build/tool/buildup --help
```

## Publishing

Add a NuGet.org API key with permission to push `Okisioli.Tools.Buildup` as the
repository Actions secret `NUGET_API_KEY`. Run **Publish NuGet tool** manually
from GitHub Actions, selecting the branch or tag to publish. The workflow builds,
packs, verifies tool installation, and pushes version `1.0.0` to NuGet.org.
NuGet versions are immutable; subsequent releases must update the version,
package release notes, and workflow version references before publishing.
The manual workflow becomes available in Actions once it is on the default branch.

# Behavior

- If the current folder is a repo root, Buildup scans that repo. It preserves nested repos. To clean a nested repo, run Buildup from inside it.
- If the current folder is inside a repo, such as `C:\work\app\src`, Buildup walks up to `C:\work\app` and scans the whole repo, including folders alongside src.
- If the current folder has no containing repo, Buildup can check parents all the way to file system root for a `.git` marker. If it finds none, it scans for repos under the original folder and ends discovery along each branch at the first repo. The upward check stops if it reaches a symlink or junction.
- Confirming permanently deletes eligible ignored files of any age and removes ignored directories only when empty. Buildup rechecks Git rules, limits deletion to previewed paths, and preserves tracked files, Git metadata, symlinks, and junctions.

- The interactive tree starts with all validated repositories and their ancestor folders, then removes branches with no eligible artifacts as counting finishes. Calculations rotate across top-level folders, with one repository per folder active and up to four workers (limited by available processors). Delete is blocked while the selected branch is still calculating.
