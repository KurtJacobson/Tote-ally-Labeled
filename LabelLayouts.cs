namespace ToteLabels;

// All positions and sizes are in printer dots at 203 dpi (8 dots per mm).
// ZplBuilder scales them for 300 dpi printers, and the web page uses them for the preview.
public record LabelLayout(
    string Id,
    string Name,
    double WidthInches,
    double HeightInches,
    Box? Logo,
    TextBlock Title,
    Rule? Divider,
    TextBlock? Contents)
{
    public int WidthDots => (int)Math.Round(WidthInches * 203);
    public int HeightDots => (int)Math.Round(HeightInches * 203);

    // The printer can't print wider than its 4.1 in head, so wider labels
    // go through on a narrower roll turned sideways.
    public bool Sideways => WidthInches > 4.1;
}

public record Box(int X, int Y, int Width, int Height);

public record Rule(int X, int Y, int Width, int Thickness);

// Align is a ZPL field block justification: "L" (left) or "C" (center).
public record TextBlock(int X, int Y, int Width, int FontSize, int MaxLines, int LineGap, string Align);

public static class LabelLayouts
{
    public static readonly LabelLayout[] All =
    [
        new LabelLayout("4x6", "4 × 6 in", 4, 6,
            Logo: new Box(106, 40, 600, 160),
            Title: new TextBlock(30, 230, 752, 120, 2, 0, "C"),
            Divider: new Rule(30, 490, 752, 4),
            Contents: new TextBlock(40, 530, 732, 60, 9, 10, "L")),

        new LabelLayout("6x4", "6 × 4 in", 6, 4,
            Logo: new Box(359, 24, 500, 140),
            Title: new TextBlock(30, 180, 1158, 110, 2, 0, "C"),
            Divider: new Rule(30, 415, 1158, 4),
            Contents: new TextBlock(40, 445, 1138, 56, 5, 8, "L")),

        new LabelLayout("2.25x1.25", "2.25 × 1.25 in", 2.25, 1.25,
            Logo: null,
            Title: new TextBlock(16, 16, 425, 70, 1, 0, "C"),
            Divider: new Rule(16, 98, 425, 3),
            Contents: new TextBlock(16, 112, 425, 36, 3, 6, "C")),
    ];

    public static LabelLayout? Find(string id) => All.FirstOrDefault(layout => layout.Id == id);
}
