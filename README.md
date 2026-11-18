# Usage

- // TO BE ADDED

# Behavior

- If the current folder is a repo root, Buildup scans that repo. It preserves nested repos. To clean a nested repo, run Buildup from inside it.
- If the current folder is inside a repo, such as `C:\work\app\src`, Buildup walks up to `C:\work\app` and scans the whole repo, including folders alongside src.
- If the current folder has no containing repo, Buildup can check parents all the way to file system root for a `.git` marker. If it finds none, it scans for repos under the original folder and ends discovery along each branch at the first repo. The upward check stops if it reaches a symlink or junction.
- Confirming permanently deletes eligible ignored files of any age and removes ignored directories only when empty. Buildup rechecks Git rules, limits deletion to previewed paths, and preserves tracked files, Git metadata, symlinks, and junctions.

- In an interactive terminal, repository discovery and validation finish first, then the folder tree opens while sizes calculate in the background. A white spinner and gray folder name indicate pending calculation; sizes update in yellow. Folders appear only after eligible artifacts are found; empty branches are omitted. Delete shows `Still calculating, please wait` until the selected folder and all its repositories finish. Press Delete again after counting finishes to confirm cleanup. Esc exits and cancels pending calculation.
