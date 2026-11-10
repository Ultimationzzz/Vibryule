using System.Drawing.Drawing2D;

namespace SpaceUI;

/// <summary>Compact, keyboard-accessible slider matching the studio palette.</summary>
public class SpaceSliderTrack : Control, ISpaceThemeAware
{
    private readonly SpaceInteraction interaction;
    private int value;

    [System.ComponentModel.DefaultValue(0)]
    public int Value
    {
        get => value;
        set
        {
            int next = Math.Clamp(value, 0, 1000);
            if (this.value == next)
                return;
            this.value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? ValueChanged;

    public SpaceSliderTrack()
    {
        DoubleBuffered = true;
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        Cursor = Cursors.Hand;
        Height = 28;
        ResizeRedraw = true;
        interaction = new SpaceInteraction(this);
    }

    // The track is painted over whichever surface hosts the slider.
    public void ApplyTheme(SpacePalette palette)
    {
        ForeColor = palette.Text;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode)
            is Keys.Left
                or Keys.Right
                or Keys.Up
                or Keys.Down
                or Keys.Home
                or Keys.End
                or Keys.PageUp
                or Keys.PageDown
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                Value -= 10;
                break;
            case Keys.Right:
            case Keys.Up:
                Value += 10;
                break;
            case Keys.Home:
                Value = 0;
                break;
            case Keys.End:
                Value = 1000;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void SetMouse(int x)
    {
        double inset = 8d * DeviceDpi / 96;
        Value = (int)
            Math.Round(Math.Clamp((x - inset) / Math.Max(1, Width - inset * 2), 0, 1) * 1000);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Capture = true;
            SetMouse(e.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Capture)
            SetMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
            Capture = false;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(SpaceDrawing.EffectiveBackColor(Parent));

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = SpaceStyle.GetPalette(this);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f,
            y = Height / 2f,
            inset = 8 * scale,
            x = inset + Math.Max(0, Width - inset * 2) * Value / 1000f,
            grow = Math.Max(interaction.Hover, interaction.Press);
        using var track = new Pen(Enabled ? palette.Border : palette.ControlDisabled, 4 * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        using var fill = new Pen(Enabled ? palette.Accent : palette.TextDisabled, 4 * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        e.Graphics.DrawLine(track, inset, y, Math.Max(inset, Width - inset), y);
        if (x > inset)
            e.Graphics.DrawLine(fill, inset, y, x, y);
        float radius = (6 + grow) * scale;
        if (Focused && ShowFocusCues)
        {
            using var ring = new SolidBrush(palette.FocusRing);
            e.Graphics.FillEllipse(
                ring,
                x - radius - 3 * scale,
                y - radius - 3 * scale,
                (radius + 3 * scale) * 2,
                (radius + 3 * scale) * 2
            );
        }
        using var thumb = new SolidBrush(Enabled ? palette.Text : palette.TextDisabled);
        e.Graphics.FillEllipse(thumb, x - radius, y - radius, radius * 2, radius * 2);
        using var core = new SolidBrush(Enabled ? palette.Accent : palette.ControlDisabled);
        float inner = radius - 3 * scale;
        e.Graphics.FillEllipse(core, x - inner, y - inner, inner * 2, inner * 2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            interaction.Dispose();
        base.Dispose(disposing);
    }
}
