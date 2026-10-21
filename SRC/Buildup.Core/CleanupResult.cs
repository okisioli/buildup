namespace Buildup.Core;

public sealed record CleanupResult(int Files, int Directories, IReadOnlyList<string> Errors);
