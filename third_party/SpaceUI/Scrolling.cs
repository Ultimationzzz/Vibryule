using System.ComponentModel;

namespace SpaceUI;

/// <summary>Vertical scrollbar with pixel-independent range and no idle timer.</summary>
[DefaultEvent(nameof(ValueChanged))]
public class SpaceScrollBar : Control, ISpaceThemeAware
{
    private int maximum, value, viewport = 100;
    private bool hover, dragging;
    private int dragY, dragValue;
    public event EventHandler? ValueChanged;
    [DefaultValue(24)] public int SmallChange { get; set; } = 24;
    [DefaultValue(0)] public int Maximum { get => maximum; set { int next = Math.Max(0, value); if (maximum == next) return; maximum = next; Value = Math.Min(this.value, maximum); Invalidate(); } }
    [DefaultValue(100)] public int ViewportSize { get => viewport; set { int next = Math.Max(1, value); if (viewport == next) return; viewport = next; Invalidate(); } }
    [DefaultValue(0)] public int Value
    {
        get => value;
        set
        {
            int next = Math.Clamp(value, 0, maximum); if (this.value == next) return;
            var old = Thumb; this.value = next; Invalidate(Rectangle.Union(old, Thumb));
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    protected Rectangle Thumb
    {
        get
        {
            int height = Math.Min(Height, Math.Max((int)(28 * DeviceDpi / 96f), (int)(Height * (viewport / ((double)maximum + viewport)))));
            int top = maximum == 0 ? 0 : (int)Math.Round((Height - height) * (value / (double)maximum));
            return new Rectangle(3, top, Math.Max(1, Width - 6), height);
        }
    }
    public SpaceScrollBar()
    {
        Width = 14; TabStop = true; AccessibleRole = AccessibleRole.ScrollBar;
        BackColor = SpaceTheme.Background;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    public void ApplyTheme(SpacePalette palette) { BackColor = SpaceDrawing.EffectiveBackColor(Parent); ForeColor = palette.Text; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (maximum > 0) { var palette = SpaceStyle.GetPalette(this); using (SpaceDrawing.Smooth(e.Graphics)) SpaceDrawing.FillRoundRect(e.Graphics, Thumb, 4 * DeviceDpi / 96f, !Enabled ? palette.Border : hover || dragging ? palette.ScrollThumbHot : palette.ScrollThumb); }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        base.OnPaint(e);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(Thumb); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(Thumb); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus();
        if (Thumb.Contains(e.Location)) { dragging = true; dragY = e.Y; dragValue = value; Capture = true; }
        else Value += e.Y < Thumb.Top ? -viewport : viewport;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging && Height > Thumb.Height) Value = (int)Math.Clamp(dragValue + (e.Y - dragY) * (double)maximum / (Height - Thumb.Height), 0, maximum);
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(Thumb); }
    protected override void OnMouseCaptureChanged(EventArgs e) { dragging = false; base.OnMouseCaptureChanged(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        int next = e.KeyCode switch { Keys.Up => value - Math.Max(1, SmallChange), Keys.Down => value + Math.Max(1, SmallChange), Keys.PageUp => value - viewport, Keys.PageDown => value + viewport, Keys.Home => 0, Keys.End => maximum, _ => value };
        if (next != value) { Value = next; e.Handled = true; } base.OnKeyDown(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e) { Value -= e.Delta / 120 * 48; base.OnMouseWheel(e); }
}

/// <summary>A vertically scrolling viewport. Add one top-docked or explicitly sized control to Content.</summary>
public class SpaceScrollPanel : UserControl
{
    private readonly Panel viewport = new() { Dock = DockStyle.Fill };
    private readonly Panel content = new() { AutoSize = false };
    private readonly SpaceScrollBar scrollbar = new() { Dock = DockStyle.Right, AccessibleName = "Scroll content" };
    private bool arranging;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)] public Panel Content => content;
    public SpaceScrollPanel()
    {
        BackColor = SpaceTheme.Background; viewport.BackColor = content.BackColor = BackColor;
        Controls.Add(viewport); Controls.Add(scrollbar); viewport.Controls.Add(content);
        scrollbar.ValueChanged += (_, _) => content.Top = -scrollbar.Value;
        viewport.SizeChanged += (_, _) => Arrange();
        content.ControlAdded += (_, e) => { if (e.Control != null) { e.Control.SizeChanged += ChildSizeChanged; e.Control.MouseWheel += ContentWheel; } Arrange(); };
        content.ControlRemoved += (_, e) => { if (e.Control != null) { e.Control.SizeChanged -= ChildSizeChanged; e.Control.MouseWheel -= ContentWheel; } Arrange(); };
        viewport.MouseWheel += ContentWheel; content.MouseWheel += ContentWheel;
    }
    private void ChildSizeChanged(object? sender, EventArgs e) => Arrange();
    private void ContentWheel(object? sender, MouseEventArgs e)
    {
        if (e is HandledMouseEventArgs { Handled: true }) return;
        scrollbar.Value -= e.Delta / 120 * 48;
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }
    protected override void OnMouseWheel(MouseEventArgs e) { ContentWheel(this, e); base.OnMouseWheel(e); }
    private void Arrange()
    {
        if (arranging) return; arranging = true;
        try
        {
            int height = content.Controls.Cast<Control>().Select(c => c.Bottom + c.Margin.Bottom).DefaultIfEmpty(0).Max();
            scrollbar.Visible = height > viewport.Height;
            content.Width = viewport.Width; content.Height = Math.Max(height, viewport.Height);
            scrollbar.ViewportSize = Math.Max(1, viewport.Height); scrollbar.Maximum = Math.Max(0, height - viewport.Height);
            content.Top = -scrollbar.Value;
        }
        finally { arranging = false; }
    }
}
