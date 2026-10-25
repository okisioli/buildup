using Spectre.Console;

internal static class Constants
{
    public static readonly Style FolderName = new(foreground: null, background: null);
    public static readonly Style SelectedFolder = new(foreground: null, background: null,
        decoration: Decoration.Bold | Decoration.Invert);
    public static readonly Style ConfirmationFolder = new(foreground: new Color(255, 255, 255),
        background: new Color(255, 0, 0));
    public static readonly Style ConfirmationDescendant = new(foreground: null, background: null);
    public static readonly Style CompletedFolder = new(foreground: Color.Red, background: null);
    public static readonly Style FolderSize = new(foreground: Color.Yellow, background: null,
        decoration: Decoration.Bold);
    public static readonly Style ConfirmationSize = new(foreground: Color.Cyan, background: null);
    public static readonly Style SavedSize = new(foreground: Color.Cyan, background: null);
    public static readonly Style TotalSavedSize = new(foreground: Color.Cyan, background: null,
        decoration: Decoration.Bold);
    public static readonly Style ConfirmationMessage = new(foreground: new Color(255, 0, 0), background: null);
    public static readonly Style SkippedLabel = new(foreground: Color.Yellow, background: null);
    public static readonly Style SkippedDetail = new(foreground: Color.Grey, background: null);
    public static readonly Style ScanDetail = new(foreground: Color.Grey, background: null);
    public static readonly Style ProgressFilled = new(foreground: Color.Cyan, background: null);
    public static readonly Style ProgressRemaining = new(foreground: Color.Grey, background: null);
}
