using System.Drawing;
using System.Text;

namespace ToteLabels;

// IconPng is the chosen icon as a PNG data URL, drawn by the browser at the icon's printed size, or null for none.
public record PrintRequest(string LayoutId, string Title, string? Contents, int Copies, string? IconPng = null);

public static class ZplBuilder
{
    public static string Label(LabelLayout layout, PrintRequest request, AppSettings settings, string? logoPath)
    {
        double scale = settings.Dpi / 203.0;
        int S(double dots) => (int)Math.Round(dots * scale);

        // Layouts are drawn the way the label reads. A sideways label goes through the
        // printer turned a quarter turn, so its height runs across the print head.
        bool sideways = layout.Sideways;
        int length = S(sideways ? layout.WidthDots : layout.HeightDots);
        int offset = (int)Math.Round(settings.TopOffsetMm / 25.4 * settings.Dpi);

        using var logoImage = layout.Logo is not null && logoPath is not null ? new Bitmap(logoPath) : null;
        using var iconImage = string.IsNullOrEmpty(request.IconPng) ? null : FromDataUrl(request.IconPng);
        var title = iconImage is null ? layout.Title : layout.TitleBesideIcon;

        // The offset moves the fields down the label rather than using the printer's own label top (^LT):
        // ^LT makes the printer feed further than the label, which builds up until it skips a label,
        // and it stays set on the printer for every other program too.
        // If moving down would push the bottom past the end of the label, shrink the layout to fit.
        var (_, top, lowest) = Fields(1, 0, draw: false);
        double fit = offset > 0 && lowest + offset > length ? (double)(length - offset - 1) / lowest : 1;
        if (fit < 1)
            (_, top, lowest) = Fields(fit, 0, draw: false);
        offset = Math.Clamp(offset, -top, Math.Max(0, length - lowest));  // never off either end
        var (fields, _, _) = Fields(fit, offset, draw: true);

        var zpl = new StringBuilder();
        zpl.AppendLine("^XA");
        zpl.AppendLine("^CI28");                       // text is UTF-8
        zpl.AppendLine($"^PW{S(sideways ? layout.HeightDots : layout.WidthDots)}");   // label width
        zpl.AppendLine($"^LL{length}");                // label length
        zpl.AppendLine("^LT0");                        // undo any label top an older version left set
        zpl.Append(fields);
        zpl.AppendLine($"^PQ{request.Copies}");
        zpl.AppendLine("^XZ");
        return zpl.ToString();

        // Builds the fields at the given size (1 is full size), moved down the label by shift dots,
        // and reports the highest and lowest printer rows they reach. Shrinking pulls everything toward
        // the top edge as the label feeds and keeps it centred across the head, so text wraps the same
        // way at any size. Measuring (draw: false) skips the slow image conversion.
        (string Zpl, int Top, int Lowest) Fields(double fit, int shift, bool draw)
        {
            int topRow = int.MaxValue, lowestRow = 0;

            // Fits a box given in layout dots, then returns its printer position and its size as the label reads.
            (int X, int Y, int W, int H) Place(double x, double y, double w, double h)
            {
                if (sideways) y = layout.HeightDots / 2.0 + (y + h / 2 - layout.HeightDots / 2.0) * fit - h * fit / 2;
                else x = layout.WidthDots / 2.0 + (x + w / 2 - layout.WidthDots / 2.0) * fit - w * fit / 2;
                if (sideways) x *= fit; else y *= fit;

                int left = S(x), top = S(y), width = S(w * fit), height = S(h * fit);
                var origin = sideways ? (S(layout.HeightDots) - top - height, left + shift) : (left, top + shift);
                topRow = Math.Min(topRow, origin.Item2);
                lowestRow = Math.Max(lowestRow, origin.Item2 + (sideways ? width : height));
                return (origin.Item1, origin.Item2, width, height);
            }

            // Every line ends with a break (\&), including the last. Zebra printers centre a line
            // without one a little to the left of the others.
            string Text(TextBlock block, IEnumerable<string> lines)
            {
                int font = S(block.FontSize * fit), gap = S(block.LineGap * fit);
                var (left, top, width, _) = Place(block.X, block.Y, block.Width, block.MaxLines * (block.FontSize + block.LineGap));
                return $"^FO{left},{top}^A0{(sideways ? 'R' : 'N')},{font},{font}" +
                       $"^FB{width},{block.MaxLines},{gap},{block.Align}^FD{string.Concat(lines.Select(line => line + @"\&"))}^FS";
            }

            var fields = new StringBuilder();

            void Image(Bitmap image, Box at)
            {
                var box = Place(at.X, at.Y, at.Width, at.Height);
                var (across, down) = sideways ? (box.H, box.W) : (box.W, box.H);
                if (draw)
                    fields.AppendLine(Graphic(image, box.X, box.Y, across, down, sideways));
            }

            if (layout.Logo is { } logo && logoImage is not null)
                Image(logoImage, logo);
            if (iconImage is not null)
                Image(iconImage, layout.Icon);

            fields.AppendLine(Text(title, [Clean(request.Title)]));

            if (layout.Divider is { } rule)
            {
                var (left, top, width, thickness) = Place(rule.X, rule.Y, rule.Width, rule.Thickness);
                var (across, down) = sideways ? (thickness, width) : (width, thickness);
                fields.AppendLine($"^FO{left},{top}^GB{across},{down},{Math.Min(across, down)}^FS");
            }

            if (layout.Contents is { } contents && !string.IsNullOrWhiteSpace(request.Contents))
                fields.AppendLine(Text(contents, request.Contents.TrimEnd().Split('\n').Select(Clean)));

            return (fields.ToString(), topRow, lowestRow);
        }
    }

    // Converts an image to a 1-bit ZPL graphic, scaled to fit and centered in the given printer box.
    // On a sideways label the box is already turned, and the image is turned a quarter turn to match the text.
    static string Graphic(Bitmap source, int x, int y, int boxWidth, int boxHeight, bool sideways)
    {
        int maxWidth = sideways ? boxHeight : boxWidth;    // the box as the label reads
        int maxHeight = sideways ? boxWidth : boxHeight;
        double fit = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);

        using var image = new Bitmap(source, Math.Max(1, (int)(source.Width * fit)), Math.Max(1, (int)(source.Height * fit)));
        if (sideways)
            image.RotateFlip(RotateFlipType.Rotate90FlipNone);  // clockwise, like ^A0R text
        int width = image.Width;
        int height = image.Height;
        int left = x + (boxWidth - width) / 2;
        int top = y + (boxHeight - height) / 2;

        // 1 bit per pixel, 8 pixels per byte, written as hex.
        int bytesPerRow = (width + 7) / 8;
        var hex = new StringBuilder();
        for (int row = 0; row < height; row++)
        {
            for (int byteIndex = 0; byteIndex < bytesPerRow; byteIndex++)
            {
                int value = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    int column = byteIndex * 8 + bit;
                    if (column < width && IsDark(image.GetPixel(column, row)))
                        value |= 0x80 >> bit;
                }
                hex.Append(value.ToString("X2"));
            }
        }

        int totalBytes = bytesPerRow * height;
        return $"^FO{left},{top}^GFA,{totalBytes},{totalBytes},{bytesPerRow},{hex}^FS";
    }

    // "data:image/png;base64,iVBOR..." to a Bitmap. Throws FormatException or ArgumentException if it isn't an image.
    static Bitmap FromDataUrl(string dataUrl)
    {
        if (!dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
            throw new FormatException("Not a PNG data URL.");
        using var stream = new MemoryStream(Convert.FromBase64String(dataUrl[(dataUrl.IndexOf(',') + 1)..]));
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);  // a copy, so it no longer needs the stream
    }

    // Transparent and light pixels stay white; everything else prints black.
    static bool IsDark(Color c) => c.A > 128 && c.GetBrightness() < 0.5f;

    // ^ and ~ are ZPL command characters, so strip them from user text.
    static string Clean(string text) => text.Trim().Replace("^", "").Replace("~", "");
}
