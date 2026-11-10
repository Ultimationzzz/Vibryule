using System.Drawing.Drawing2D;

namespace SpaceUI;

/// <summary>Small drawing helpers shared by the editor and the application; all temporary GDI resources are scoped.</summary>
public static class SpaceDrawing
{
    public static Color Mix(Color first, Color second, double amount) =>
        SpacePalette.Mix(first, second, amount);

    public static Color Alpha(Color color, int alpha) =>
        Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    public static Color EffectiveBackColor(Control? control)
    {
        while (control != null)
        {
            if (control.BackColor.A == 255)
                return control.BackColor;
            control = control.Parent;
        }
        return SpacePalette.Default.Surface;
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Clamp(radius * 2, 0, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundRect(
        Graphics graphics,
        RectangleF bounds,
        float radius,
        Color color
    )
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        using var path = Rounded(bounds, radius);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundRect(
        Graphics graphics,
        RectangleF bounds,
        float radius,
        Color color,
        float width = 1
    )
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        using var path = Rounded(bounds, radius);
        using var pen = new Pen(color, width);
        graphics.DrawPath(pen, path);
    }

    public static void DrawFocusRing(
        Graphics graphics,
        RectangleF bounds,
        float radius,
        SpacePalette palette
    ) => DrawRoundRect(graphics, bounds, radius, palette.FocusRing, 1.5f);

    public static void DrawText(
        Graphics graphics,
        string text,
        Font font,
        Rectangle bounds,
        Color color,
        TextFormatFlags flags
    ) => TextRenderer.DrawText(graphics, text, font, bounds, color, flags);

    public static TextFormatFlags TextFlags(
        Control control,
        ContentAlignment alignment = ContentAlignment.MiddleLeft
    )
    {
        var flags =
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        flags |=
            alignment
                is ContentAlignment.TopCenter
                    or ContentAlignment.MiddleCenter
                    or ContentAlignment.BottomCenter
                ? TextFormatFlags.HorizontalCenter
            : alignment
                is ContentAlignment.TopRight
                    or ContentAlignment.MiddleRight
                    or ContentAlignment.BottomRight
                ? TextFormatFlags.Right
            : TextFormatFlags.Left;
        flags |=
            alignment
                is ContentAlignment.MiddleLeft
                    or ContentAlignment.MiddleCenter
                    or ContentAlignment.MiddleRight
                ? TextFormatFlags.VerticalCenter
            : alignment
                is ContentAlignment.BottomLeft
                    or ContentAlignment.BottomCenter
                    or ContentAlignment.BottomRight
                ? TextFormatFlags.Bottom
            : TextFormatFlags.Top;
        if (control.RightToLeft == RightToLeft.Yes)
            flags |= TextFormatFlags.RightToLeft;
        return flags;
    }

    public static IDisposable Smooth(Graphics graphics) => new SmoothingScope(graphics);

    private sealed class SmoothingScope : IDisposable
    {
        private readonly Graphics graphics;
        private readonly SmoothingMode previous;

        public SmoothingScope(Graphics graphics)
        {
            this.graphics = graphics;
            previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
        }

        public void Dispose() => graphics.SmoothingMode = previous;
    }
}

/// <summary>Event-driven interaction state. No animation timer or idle invalidation.</summary>
public sealed class SpaceInteraction : IDisposable
{
    private readonly Control control;
    public float Hover { get; private set; }
    public float Press { get; private set; }

    public SpaceInteraction(Control control)
    {
        this.control = control;
        control.MouseEnter += Enter;
        control.MouseLeave += Leave;
        control.MouseDown += Down;
        control.MouseUp += Up;
        control.MouseCaptureChanged += CaptureChanged;
        control.EnabledChanged += EnabledChanged;
    }

    private void Set(float hover, float press)
    {
        if (Hover == hover && Press == press)
            return;
        Hover = hover;
        Press = press;
        control.Invalidate();
    }

    private void Enter(object? sender, EventArgs e)
    {
        if (control.Enabled)
            Set(1, Press);
    }

    private void Leave(object? sender, EventArgs e) => Set(0, Press);

    private void Down(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Set(Hover, 1);
    }

    private void Up(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Set(Hover, 0);
    }

    private void CaptureChanged(object? sender, EventArgs e)
    {
        if (!control.Capture)
            Set(Hover, 0);
    }

    private void EnabledChanged(object? sender, EventArgs e)
    {
        if (!control.Enabled)
            Set(0, 0);
    }

    public void Dispose()
    {
        control.MouseEnter -= Enter;
        control.MouseLeave -= Leave;
        control.MouseDown -= Down;
        control.MouseUp -= Up;
        control.MouseCaptureChanged -= CaptureChanged;
        control.EnabledChanged -= EnabledChanged;
    }
}
