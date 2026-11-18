namespace Buildup.Core;

// Each update owns a stable snapshot; the UI never reads the worker's mutable collections.
public sealed record CleanupCalculation(CleanupPlan Plan, IReadOnlyList<string> CompletedRepositories);
