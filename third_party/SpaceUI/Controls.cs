using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace SpaceUI;

/// <summary>Palette from irp-ui/html/styles/blue.css. Alpha tokens are precomposited over #1A314D.</summary>
public static class SpaceTheme
{
    public static readonly Color Background = Color.FromArgb(20, 39, 60);
    public static readonly Color Surface = Color.FromArgb(26, 49, 77);
    public static readonly Color Elevated = Color.FromArgb(35, 65, 98);
    public static readonly Color Border = Color.FromArgb(49, 70, 95);
    public static readonly Color Primary = Color.FromArgb(91, 163, 224);
    public static readonly Color PrimaryDark = Color.FromArgb(91, 163, 224);
    public static readonly Color Secondary = Color.FromArgb(141, 196, 238);
    public static readonly Color Accent = Color.FromArgb(91, 163, 224);
    public static readonly Color Success = Color.FromArgb(87, 185, 138);
    public static readonly Color Warning = Color.FromArgb(217, 188, 97);
    public static readonly Color Danger = Color.FromArgb(217, 106, 106);
    public static readonly Color Text = Color.FromArgb(209, 214, 219);
    public static readonly Color Muted = Color.FromArgb(145, 156, 170);
    public static readonly Color Disabled = Color.FromArgb(104, 119, 138);

    /// <summary>Styles a standard control without replacing its native drawing or events.</summary>
    public static void StyleControl(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control is ISpaceThemeAware aware) { aware.ApplyTheme(SpacePalette.Default); return; }
        if (control.GetType().Assembly == typeof(SpaceTheme).Assembly) return;
        if (control.ForeColor == SystemColors.ControlText || control.ForeColor == SystemColors.WindowText)
            control.ForeColor = Text;
        switch (control)
        {
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.BackColor = PrimaryDark; button.ForeColor = Background;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Primary;
                button.FlatAppearance.MouseDownBackColor = Elevated;
                break;
            case CheckBox check: check.FlatStyle = FlatStyle.Flat; break;
            case RadioButton radio: radio.FlatStyle = FlatStyle.Flat; break;
            case TextBoxBase input: input.BackColor = Background; input.ForeColor = Text; input.BorderStyle = BorderStyle.FixedSingle; break;
            case ComboBox combo: combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Elevated; combo.ForeColor = Text; break;
            case DataGridView grid: StyleGrid(grid); break;
            case ListView list: list.BackColor = Surface; list.ForeColor = Text; break;
            case TreeView tree: tree.BackColor = Surface; tree.ForeColor = Text; tree.LineColor = Border; break;
            case ListBox listBox: listBox.BackColor = Surface; listBox.ForeColor = Text; break;
            case UpDownBase numeric: numeric.BackColor = Background; numeric.ForeColor = Text; break;
            case ToolStrip strip: strip.Renderer = new SpaceToolStripRenderer(); strip.BackColor = Surface; strip.ForeColor = Text; break;
            default:
                if (control.BackColor == SystemColors.Control) control.BackColor = Surface;
                break;
        }
        if (control.ContextMenuStrip is { } menu)
        {
            menu.Renderer = new SpaceToolStripRenderer(); menu.BackColor = Surface; menu.ForeColor = Text;
        }
    }


    public static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Surface; grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false; grid.GridColor = Elevated;
        grid.RowHeadersVisible = false; grid.AllowUserToAddRows = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Elevated;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Elevated;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
        grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(41, 75, 111); grid.DefaultCellStyle.SelectionForeColor = Text;
    }
}

internal static class Draw
{
    internal static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath(); int d = Math.Min(Math.Max(0, radius * 2), Math.Min(r.Width, r.Height));
        if (d <= 1) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    internal static void Box(Graphics g, Rectangle bounds, Color fill, Color border, int radius = 5)
    {
        if (bounds.Width <= 1 || bounds.Height <= 1) return;
        bounds.Width--; bounds.Height--;
        using var path = Rounded(bounds, radius); using var brush = new SolidBrush(fill); using var pen = new Pen(border);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(brush, path); g.DrawPath(pen, path);
    }
}


public enum SpaceButtonStyle { Primary, Secondary, Success, Warning, Danger }

/// <summary>Uses WinForms button rendering, including keyboard, focus, mnemonics and default-button behavior.</summary>
[DefaultEvent(nameof(Click))]
public class SpaceButton : Button
{
    private SpaceButtonStyle style;
    [DefaultValue(SpaceButtonStyle.Primary)]
    public SpaceButtonStyle ButtonStyle
    {
        get => style;
        set { if (style == value) return; style = value; ApplyStyle(); }
    }

    public SpaceButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        Size = new(110, 30);
        Cursor = Cursors.Hand;
        ApplyStyle();
    }

    private void ApplyStyle()
    {
        BackColor = style switch
        {
            SpaceButtonStyle.Secondary => SpaceTheme.Surface,
            SpaceButtonStyle.Success => SpaceTheme.Success,
            SpaceButtonStyle.Warning => SpaceTheme.Warning,
            SpaceButtonStyle.Danger => SpaceTheme.Danger,
            _ => SpaceTheme.PrimaryDark
        };
        ForeColor = style == SpaceButtonStyle.Secondary ? SpaceTheme.Text : SpaceTheme.Background;
        FlatAppearance.BorderColor = style == SpaceButtonStyle.Secondary ? SpaceTheme.Border : BackColor;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseOverBackColor = ControlPaint.Light(BackColor, .12f);
        FlatAppearance.MouseDownBackColor = ControlPaint.Dark(BackColor, .12f);
    }
}

public class SpacePanel : Panel
{
    public SpacePanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SpaceTheme.Surface;
        ForeColor = SpaceTheme.Text;
        Padding = new(12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Background is already erased by WinForms. Paint only the border.
        using var pen = new Pen(SpaceTheme.Border);
        if (Width > 1 && Height > 1) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        base.OnPaint(e);
    }
}

/// <summary>A native checkbox with a custom switch glyph; retains checkbox accessibility and keyboard behavior.</summary>
[DefaultEvent(nameof(CheckedChanged))]
public class SpaceToggle : CheckBox
{
    public SpaceToggle()
    {
        AutoSize = false;
        Size = new(120, 28);
        ForeColor = SpaceTheme.Text;
        BackColor = SpaceTheme.Surface;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);
        int trackWidth = S(36), trackHeight = S(18);
        int left = RightToLeft == RightToLeft.Yes ? Width - trackWidth : 0;
        var track = new Rectangle(left, (Height - trackHeight) / 2, trackWidth, trackHeight);
        var color = Enabled ? (Checked ? SpaceTheme.PrimaryDark : SpaceTheme.Elevated) : SpaceTheme.Border;
        Draw.Box(e.Graphics, track, color, color, S(9));
        using var brush = new SolidBrush(Enabled ? Color.White : SpaceTheme.Disabled);
        bool right = Checked ^ (RightToLeft == RightToLeft.Yes);
        e.Graphics.FillEllipse(brush, left + S(right ? 21 : 3), track.Y + S(3), S(12), S(12));
        var textBounds = new Rectangle(RightToLeft == RightToLeft.Yes ? 0 : S(44), 0, Math.Max(0, Width - S(44)), Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
        if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : SpaceTheme.Disabled, flags);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
    }
}

/// <summary>A static flat progress indicator. No timer; only the changed fill region is invalidated.</summary>
public class SpaceProgressBar : Control
{
    private int minimum, maximum = 100, value;
    [DefaultValue(0)]
    public int Minimum
    {
        get => minimum;
        set
        {
            if (value >= maximum) throw new ArgumentOutOfRangeException(nameof(value));
            if (minimum == value) return;
            minimum = value; this.value = Math.Max(this.value, minimum); Invalidate();
        }
    }
    [DefaultValue(100)]
    public int Maximum
    {
        get => maximum;
        set
        {
            if (value <= minimum) throw new ArgumentOutOfRangeException(nameof(value));
            if (maximum == value) return;
            maximum = value; this.value = Math.Min(this.value, maximum); Invalidate();
        }
    }
    [DefaultValue(0)]
    public int Value
    {
        get => value;
        set
        {
            if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(nameof(value));
            if (this.value == value) return;
            int previous = FillWidth;
            this.value = value;
            int current = FillWidth;
            if (previous != current) Invalidate(new Rectangle(Math.Min(previous, current), 0, Math.Abs(previous - current), Height));
            if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
    }
    private int FillWidth => (int)Math.Round(Width * (((double)value - minimum) / ((double)maximum - minimum)));
    public SpaceProgressBar()
    {
        Size = new(150, 10); TabStop = false; AccessibleRole = AccessibleRole.ProgressBar;
        ForeColor = SpaceTheme.Primary; BackColor = SpaceTheme.Elevated;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new SolidBrush(Enabled ? ForeColor : SpaceTheme.Disabled);
        e.Graphics.FillRectangle(brush, 0, 0, FillWidth, Height);
        base.OnPaint(e);
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ProgressAccessibility(this);
    private sealed class ProgressAccessibility(SpaceProgressBar owner) : ControlAccessibleObject(owner)
    {
        public override string? Value => owner.Value.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }
}

/// <summary>Native text editing, selection, shortcuts, IME, password and accessibility support.</summary>
public class SpaceTextBox : TextBox
{
    public SpaceTextBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = SpaceTheme.Background;
        ForeColor = SpaceTheme.Text;
    }
}


public class SpaceCheckBox : CheckBox
{
    public SpaceCheckBox() { AutoSize = true; FlatStyle = FlatStyle.Flat; ForeColor = SpaceTheme.Text; BackColor = SpaceTheme.Surface; }
}

public class SpaceRadioButton : RadioButton
{
    public SpaceRadioButton() { AutoSize = true; FlatStyle = FlatStyle.Flat; ForeColor = SpaceTheme.Text; BackColor = SpaceTheme.Surface; }
}
