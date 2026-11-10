using System.Drawing.Drawing2D;

namespace SpaceUI;

public class SpaceSwitch : Control, ISpaceThemeAware
{
    private readonly SpaceInteraction interaction;
    private bool isChecked;

    [System.ComponentModel.DefaultValue(false)]
    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
                return;
            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? CheckedChanged;

    protected override AccessibleObject CreateAccessibilityInstance() =>
        new ToggleAccessibility(this);

    private sealed class ToggleAccessibility(SpaceSwitch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State =>
            base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string? Name
        {
            get => owner.AccessibleName ?? owner.Text;
            set => owner.AccessibleName = value;
        }
        public override string DefaultAction => "Toggle";

        public override void DoDefaultAction()
        {
            if (owner.Enabled)
                owner.OnClick(EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
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

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    public int TextInset => (int)Math.Round(46 * DeviceDpi / 96f);

    public SpaceSwitch()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true
        );
        // Use a plain opaque control: the native CheckBox owner-paint path replayed
        // sibling labels into the custom switch during live repainting.
        SetStyle(ControlStyles.SupportsTransparentBackColor, false);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        interaction = new SpaceInteraction(this);
    }

    public void ApplyTheme(SpacePalette palette)
    {
        ForeColor = palette.Text;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(SpaceDrawing.EffectiveBackColor(Parent));

    protected override void OnPaint(PaintEventArgs e)
    {
        var palette = SpaceStyle.GetPalette(this);
        float scale = DeviceDpi / 96f,
            hover = interaction.Hover;
        var track = new RectangleF(1 * scale, Height / 2f - 9 * scale, 34 * scale, 18 * scale);
        using (SpaceDrawing.Smooth(e.Graphics))
        {
            Color fill =
                !Enabled ? palette.ControlDisabled
                : Checked ? SpaceDrawing.Mix(palette.Accent, palette.AccentHover, hover)
                : SpaceDrawing.Mix(palette.Border, palette.BorderStrong, hover);
            SpaceDrawing.FillRoundRect(e.Graphics, track, 9 * scale, fill);
            if (Focused && ShowFocusCues)
                SpaceDrawing.DrawFocusRing(
                    e.Graphics,
                    RectangleF.Inflate(track, 3 * scale, 3 * scale),
                    12 * scale,
                    palette
                );
            using var thumb = new SolidBrush(
                !Enabled ? palette.TextDisabled
                : Checked ? palette.OnAccent
                : palette.Text
            );
            e.Graphics.FillEllipse(
                thumb,
                track.X + (Checked ? 19 : 3) * scale,
                track.Y + 3 * scale,
                12 * scale,
                12 * scale
            );
        }
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            new Rectangle(TextInset, 0, Math.Max(1, Width - TextInset), Height),
            Enabled ? palette.Text : palette.TextDisabled,
            TextFormatFlags.Left
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.WordBreak
                | TextFormatFlags.NoPrefix
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            interaction.Dispose();
        base.Dispose(disposing);
    }
}
