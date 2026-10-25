using Spectre.Console;
using Spectre.Console.Rendering;

// One multiline column keeps the spinner, bar, and details stacked vertically.
internal sealed class ScanProgressColumn : ProgressColumn
{
    private readonly SpinnerColumn spinner = new() { Spinner = Spinner.Known.Line };
    private const int BarWidth = 40;

    public string Detail { get; set; } = "";

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var lines = new List<IRenderable>
        {
            new Columns(spinner.Render(options, task, deltaTime), new Text(task.Description))
        };
        if (!task.IsIndeterminate)
        {
            // Explicit ASCII glyphs keep the bar visible even without terminal colors.
            var filled = (int)(BarWidth * task.Percentage / 100);
            lines.Add(new Markup($"[[[cyan]{new string('#', filled)}[/][grey]{new string('-', BarWidth - filled)}[/]]] {task.Percentage:0}%"));
        }
        if (!string.IsNullOrEmpty(Detail))
            lines.Add(new Text(Detail, new Style(Color.Grey)));
        return new Rows(lines);
    }
}
