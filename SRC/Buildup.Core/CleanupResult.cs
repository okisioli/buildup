namespace Buildup.Core;

public sealed record CleanupResult(int Files, int Directories, long BytesDeleted, IReadOnlyList<string> Errors);
