using System.Globalization;

namespace ToteLabels;

// A label size as the user sets it up. Sizes are stored in inches; Unit is only how it was entered and is shown.
public record LabelSize(
    string Id,
    string Name,            // "" uses a name made from the size, like "4 × 6 in"
    double WidthInches,     // for a round label, width and height are both the diameter
    double HeightInches,
    string Unit,            // "in" or "mm"
    bool Round,
    bool ShowLogo,
    int TitleLines,         // 1 or 2
    string ContentsAlign);  // "L" (left) or "C" (centred)

// All positions and sizes are in printer dots at 203 dpi (8 dots per mm), on the label as it reads.
// ZplBuilder scales them for 300 dpi printers, and the web page uses them for the preview.
public record LabelLayout(LabelSize Size, string Name, Box? Logo, TextBlock Title, Rule? Divider, TextBlock? Contents)
{
    public string Id => Size.Id;
    public double WidthInches => Size.WidthInches;
    public double HeightInches => Size.HeightInches;
    public bool Round => Size.Round;
    public int WidthDots => (int)Math.Round(WidthInches * 203);
    public int HeightDots => (int)Math.Round(HeightInches * 203);

    // The printer can't print wider than its head, so wider labels
    // go through on a narrower roll turned sideways.
    public bool Sideways => WidthInches > LabelLayouts.MaxPrintWidthInches;

    // Where a chosen icon goes: a square as tall as one title line at the start of the title, with the
    // title moved over beside it and left-aligned so the two read together. On a label with the logo
    // beside the title, the title already starts after the logo, so the icon sits between them.
    public Box Icon => new(Title.X, Title.Y, Title.FontSize, Title.FontSize);

    public TextBlock TitleBesideIcon => Title with
    {
        X = Title.X + Title.FontSize + IconGap,
        Width = Title.Width - Title.FontSize - IconGap,
        Align = "L"
    };

    int IconGap => (int)Math.Round(Title.FontSize * 0.2);
}

public record Box(int X, int Y, int Width, int Height);

public record Rule(int X, int Y, int Width, int Thickness);

// Align is a ZPL field block justification: "L" (left) or "C" (center).
public record TextBlock(int X, int Y, int Width, int FontSize, int MaxLines, int LineGap, string Align);

// Works out each size's layout from its dimensions and options, so any size the user adds gets
// the same proportions as the built-in ones.
public static class LabelLayouts
{
    public const double MaxPrintWidthInches = 4.09;  // the ZD621's print head
    public const double MinInches = 0.75;
    public const double MaxLengthInches = 12;

    public static readonly LabelSize[] Defaults =
    [
        new("4x6", "", 4, 6, "in", Round: false, ShowLogo: true, TitleLines: 2, ContentsAlign: "L"),
        new("6x4", "", 6, 4, "in", Round: false, ShowLogo: true, TitleLines: 2, ContentsAlign: "L"),
        new("2.25x1.25", "", 2.25, 1.25, "in", Round: false, ShowLogo: false, TitleLines: 1, ContentsAlign: "C"),
    ];

    // Why the printer can't use this size, or null if it can.
    public static string? Problem(LabelSize size)
    {
        if (size.Round)
            size = size with { HeightInches = size.WidthInches };
        if (size.Unit is not ("in" or "mm")) return "Units must be in or mm.";
        if (size.TitleLines is not (1 or 2)) return "The title can have 1 or 2 lines.";
        if (size.ContentsAlign is not ("L" or "C")) return "Contents must be left-aligned or centred.";
        if (size.Name.Length > 40) return "Keep the name to 40 characters.";

        double small = Math.Min(size.WidthInches, size.HeightInches), large = Math.Max(size.WidthInches, size.HeightInches);
        if (double.IsNaN(small) || small < MinInches)
            return $"Labels must be at least {Length(MinInches, size.Unit)} in each direction.";
        if (size.Round && size.WidthInches > MaxPrintWidthInches)
            return $"A round label can be at most {Length(MaxPrintWidthInches, size.Unit)} across, the widest the printer prints.";
        if (small > MaxPrintWidthInches)
            return $"One side must be {Length(MaxPrintWidthInches, size.Unit)} or less, the widest the printer prints.";
        if (large > MaxLengthInches)
            return $"Labels can be at most {Length(MaxLengthInches, size.Unit)} long.";
        return null;
    }

    // logoAspect is the uploaded logo's width / height, or null when there is no logo. The logo's space is
    // sized to it, so a square logo beside the title takes a square and leaves the rest to the title.
    public static LabelLayout Build(LabelSize size, double? logoAspect)
    {
        if (size.Round)
            size = size with { HeightInches = size.WidthInches };
        return size.Round ? BuildRound(size, logoAspect) : BuildRectangle(size, logoAspect);
    }

    public static string DisplayName(LabelSize size) =>
        !string.IsNullOrWhiteSpace(size.Name) ? size.Name.Trim()
        : size.Round ? $"{Number(size.WidthInches, size.Unit)} {size.Unit} round"
        : $"{Number(size.WidthInches, size.Unit)} × {Number(size.HeightInches, size.Unit)} {size.Unit}";

    // Margin, title, divider, then as many content lines as fit. Tuned so the built-in sizes
    // come out within a few dots of the layouts they had when they were placed by hand.
    static LabelLayout BuildRectangle(LabelSize size, double? logoAspect)
    {
        int w = Dots(size.WidthInches), h = Dots(size.HeightInches);
        int margin = R(8 + Math.Min(w, h) * 0.027);
        int y = margin;

        var fonts = Fonts(Math.Min(w * 0.15, h * (size.TitleLines == 1 ? 0.28 : 0.135)));
        int titleHeight = size.TitleLines * fonts.Title;

        // The logo goes across the top when it would be at least as tall as the title text,
        // which suits tall labels; on shorter ones it goes beside the title instead.
        int logoOnTopHeight = R(Math.Min(h * 0.14, 180));
        bool showLogo = size.ShowLogo && logoAspect is not null;
        double aspect = logoAspect ?? 1;
        bool logoOnTop = showLogo && logoOnTopHeight >= fonts.Title;
        Box? logo = null;
        if (logoOnTop)
        {
            int logoWidth = Math.Min(w - 2 * margin, R(logoOnTopHeight * aspect));
            logo = new Box((w - logoWidth) / 2, y, logoWidth, logoOnTopHeight);
            y += logoOnTopHeight + margin;
        }
        int titleX = margin, titleWidth = w - 2 * margin;

        // Otherwise the logo sits beside the title, as tall as the title, and the title moves over.
        if (showLogo && !logoOnTop)
        {
            int logoWidth = Math.Min(R(titleHeight * aspect), R(w * 0.3));
            logo = new Box(margin, y, logoWidth, titleHeight);
            titleX += logoWidth + margin;
            titleWidth -= logoWidth + margin;
        }

        var title = new TextBlock(titleX, y, titleWidth, fonts.Title, size.TitleLines, 0, "C");
        y += titleHeight;

        int thickness = Math.Max(3, R(Math.Min(w, h) / 200.0));
        int dividerY = y + R(margin * 0.6);
        int contentsY = dividerY + thickness + margin;
        int lines = (h - margin - contentsY) / (fonts.Contents + fonts.Gap);
        if (lines < 1)
            return new LabelLayout(size, DisplayName(size), logo, title, null, null);  // room for a title only

        int indent = size.ContentsAlign == "L" ? 10 : 0;
        return new LabelLayout(size, DisplayName(size), logo, title,
            new Rule(margin, dividerY, w - 2 * margin, thickness),
            new TextBlock(margin + indent, contentsY, w - 2 * margin - indent, fonts.Contents, lines, fonts.Gap, size.ContentsAlign));
    }

    // The same stack, centred on the label, with each block narrowed to the width of the circle at its
    // top or bottom edge, whichever is narrower. Takes as many content lines as stay inside the circle.
    static LabelLayout BuildRound(LabelSize size, double? logoAspect)
    {
        int d = Dots(size.WidthInches);
        double centre = d / 2.0;
        int margin = R(8 + d * 0.027);
        double radius = centre - margin;  // everything stays inside this circle
        var fonts = Fonts(d * (size.TitleLines == 1 ? 0.16 : 0.11));
        int thickness = Math.Max(3, R(d / 200.0));
        int dividerGap = R(margin * 0.6);

        // Half the circle's width across a block from top to bottom, or -1 where the block leaves the circle.
        double HalfWidth(double top, double bottom)
        {
            double far = Math.Max(Math.Abs(top - centre), Math.Abs(bottom - centre));
            return far >= radius ? -1 : Math.Sqrt(radius * radius - far * far);
        }

        // Every stack that fits, from the most content lines down; null when none does.
        LabelLayout? Stack(bool withLogo)
        {
            int logoHeight = withLogo ? R(d * (size.WidthInches >= 2.5 ? 0.12 : 0.15)) : 0;

            for (int lines = 12; lines >= 0; lines--)
            {
                int contentsHeight = lines * (fonts.Contents + fonts.Gap);
                int height = (withLogo ? logoHeight + margin : 0) + size.TitleLines * fonts.Title
                             + (lines > 0 ? dividerGap + thickness + margin + contentsHeight : 0);
                double y = centre - height / 2.0;

                Box? logo = null;
                if (withLogo)
                {
                    double half = HalfWidth(y, y + logoHeight);
                    if (half < d * 0.15) continue;
                    int logoWidth = Math.Min(R(2 * half), R(logoHeight * (logoAspect ?? 1)));
                    logo = new Box(R(centre - logoWidth / 2.0), R(y), logoWidth, logoHeight);
                    y += logoHeight + margin;
                }

                double titleHalf = HalfWidth(y, y + size.TitleLines * fonts.Title);
                if (titleHalf < d * 0.3) continue;  // the title keeps at least 60% of the diameter
                var title = new TextBlock(R(centre - titleHalf), R(y), R(2 * titleHalf), fonts.Title, size.TitleLines, 0, "C");
                y += size.TitleLines * fonts.Title;

                if (lines == 0)
                    return new LabelLayout(size, DisplayName(size), logo, title, null, null);

                double dividerY = y + dividerGap;
                double contentsY = dividerY + thickness + margin;
                double ruleHalf = HalfWidth(dividerY, dividerY + thickness);
                double contentsHalf = HalfWidth(contentsY, contentsY + contentsHeight);
                if (contentsHalf < d * 0.275) continue;  // and the contents 55%: fewer, wider lines beat more narrow ones

                return new LabelLayout(size, DisplayName(size), logo, title,
                    new Rule(R(centre - ruleHalf), R(dividerY), R(2 * ruleHalf), thickness),
                    new TextBlock(R(centre - contentsHalf), R(contentsY), R(2 * contentsHalf), fonts.Contents, lines, fonts.Gap, size.ContentsAlign));
            }
            return null;
        }

        // With the logo if it fits and still leaves room for contents; otherwise without it,
        // rather than losing the contents to make room.
        var withLogo = size.ShowLogo && logoAspect is not null ? Stack(true) : null;
        var withoutLogo = Stack(false);
        if (withLogo is not null && (withLogo.Contents is not null || withoutLogo?.Contents is null))
            return withLogo;
        if (withoutLogo is not null)
            return withoutLogo;

        // Too small for even the title to sit inside the circle comfortably: use the full width at the middle.
        var fallback = Fonts(d * 0.14);
        return new LabelLayout(size, DisplayName(size), null,
            new TextBlock(margin, R(centre - fallback.Title / 2.0), d - 2 * margin, fallback.Title, 1, 0, "C"), null, null);
    }

    // The title font from a target size, and the contents font and line gap that go with it.
    static (int Title, int Contents, int Gap) Fonts(double title)
    {
        int titleFont = Math.Clamp(R(title), 24, 160);
        int contentsFont = Math.Clamp(R(titleFont * 0.5), 20, 64);
        return (titleFont, contentsFont, R(contentsFont * 0.16));
    }

    static int Dots(double inches) => R(inches * 203);

    static int R(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    static string Number(double inches, string unit) =>
        unit == "mm" ? Math.Round(inches * 25.4, 1).ToString("0.#", CultureInfo.InvariantCulture)
        : Math.Round(inches, 3).ToString("0.###", CultureInfo.InvariantCulture);

    static string Length(double inches, string unit) => $"{Number(inches, unit)} {unit}";
}
