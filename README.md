# Usage

- // TO BE ADDED

# Behavior

- If the current folder is a repo root, Buildup scans that repo. It preserves nested repos. To clean a nested repo, run Buildup from inside it.
- If the current folder is inside a repo, such as `C:\work\app\src`, Buildup walks up to `C:\work\app` and scans the whole repo, including folders alongside src.
- If the current folder has no containing repo, Buildup can check parents all the way to file system root for a `.git` marker. If it finds none, it scans for repos under the original folder and ends discovery along each branch at the first repo. The upward check stops if it reaches a symlink or junction.
- Confirming permanently deletes eligible ignored files of any age and removes ignored directories only when empty. Buildup rechecks Git rules, limits deletion to previewed paths, and preserves tracked files, Git metadata, symlinks, and junctions.

- The interactive tree starts with all validated repositories and their ancestor folders, then removes branches with no eligible artifacts as counting finishes. Calculations rotate across top-level folders, with one repository per folder active and up to four workers (limited by available processors).
- While browsing, press **Delete** to mark or unmark a folder. Once the entire scan finishes, press **Enter** on each marked folder you want to clean, including its descendants.
