namespace Okisioli.Tools.Buildup.Core;

public sealed record CleanupEntry(string Path, bool IsDirectory, long Bytes = 0);
