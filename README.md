# Buildup

- Run `buildup` with Git on your PATH. It scans the nearest containing Git repository and any nested repositories. From outside a repository, it looks for repositories under the current directory.
- It previews folders with ignored files or empty ignored directories and estimates how much space cleanup could save. It skips repositories that Git rejects and shows why.
- In an interactive terminal, use the arrow keys to browse and expand folders, press Delete to select a folder and its descendants for cleanup, then press Enter to confirm. Redirected or non-interactive sessions only show the preview.
- Confirming permanently deletes eligible ignored files of any age and removes ignored directories only when empty. Buildup rechecks Git rules, limits deletion to previewed paths, and preserves tracked files, Git metadata, symlinks, and junctions.
- After cleanup, Buildup reports how much space it saved based on the sizes of the files it deleted.
