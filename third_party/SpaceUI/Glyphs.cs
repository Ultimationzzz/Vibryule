using System.Drawing.Drawing2D;

namespace SpaceUI;

public enum SpaceGlyph
{
    None,
    Camera,
    Chat,
    ChevronDown,
    ChevronRight,
    Copy,
    Download,
    Edit,
    ExternalLink,
    Eye,
    EyeOff,
    Face,
    Folder,
    Info,
    Layers,
    List,
    Mic,
    Minus,
    Monitor,
    Play,
    Plus,
    Search,
    Settings,
    Sliders,
    Trash,
    User,
    Wand,
}

/// <summary>Vector paint for frequently drawn glyphs; Windows symbol glyphs for cached button images.</summary>
public static class SpaceGlyphs
{
    public static void Draw(
        Graphics graphics,
        SpaceGlyph glyph,
        RectangleF bounds,
        Color color,
        float width = 1.5f
    )
    {
        if (glyph == SpaceGlyph.None || bounds.Width <= 0 || bounds.Height <= 0)
            return;
        using var smoothing = SpaceDrawing.Smooth(graphics);
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        float left = bounds.Left + bounds.Width * .2f,
            right = bounds.Right - bounds.Width * .2f;
        float top = bounds.Top + bounds.Height * .2f,
            bottom = bounds.Bottom - bounds.Height * .2f;
        float x = bounds.Left + bounds.Width / 2,
            y = bounds.Top + bounds.Height / 2;
        switch (glyph)
        {
            case SpaceGlyph.Plus:
                graphics.DrawLine(pen, x, top, x, bottom);
                goto case SpaceGlyph.Minus;
            case SpaceGlyph.Minus:
                graphics.DrawLine(pen, left, y, right, y);
                return;
            case SpaceGlyph.ChevronDown:
                graphics.DrawLines(
                    pen,
                    new PointF[]
                    {
                        new(left, top + bounds.Height * .15f),
                        new(x, bottom),
                        new(right, top + bounds.Height * .15f),
                    }
                );
                return;
            case SpaceGlyph.ChevronRight:
                graphics.DrawLines(
                    pen,
                    new PointF[] { new(left, top), new(right, y), new(left, bottom) }
                );
                return;
            case SpaceGlyph.Search:
                graphics.DrawEllipse(pen, left, top, bounds.Width * .45f, bounds.Height * .45f);
                graphics.DrawLine(
                    pen,
                    x + bounds.Width * .1f,
                    y + bounds.Height * .1f,
                    right,
                    bottom
                );
                return;
            default:
            {
                string symbol = glyph switch
                {
                    SpaceGlyph.Camera => "\uE722",
                    SpaceGlyph.Chat => "\uE8BD",
                    SpaceGlyph.Copy => "\uE8C8",
                    SpaceGlyph.Download => "\uE896",
                    SpaceGlyph.Edit => "\uE70F",
                    SpaceGlyph.ExternalLink => "\uE8A7",
                    SpaceGlyph.Eye => "\uE890",
                    SpaceGlyph.EyeOff => "\uED1A",
                    SpaceGlyph.Face => "\uE899",
                    SpaceGlyph.Folder => "\uE8B7",
                    SpaceGlyph.Info => "\uE946",
                    SpaceGlyph.Layers => "\uE81E",
                    SpaceGlyph.List => "\uE8A4",
                    SpaceGlyph.Mic => "\uE720",
                    SpaceGlyph.Monitor => "\uE7F4",
                    SpaceGlyph.Play => "\uE768",
                    SpaceGlyph.Settings => "\uE713",
                    SpaceGlyph.Sliders => "\uE9E9",
                    SpaceGlyph.Trash => "\uE74D",
                    SpaceGlyph.User => "\uE77B",
                    _ => "\uE735",
                };
                using var font = new Font(
                    "Segoe MDL2 Assets",
                    Math.Max(1, bounds.Height * .85f),
                    GraphicsUnit.Pixel
                );
                using var brush = new SolidBrush(color);
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                };
                graphics.DrawString(symbol, font, brush, bounds, format);
                return;
            }
        }
    }
}
