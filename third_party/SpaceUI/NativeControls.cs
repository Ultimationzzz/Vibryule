using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpaceUI;

/// <summary>A standard resizable Form with the SpaceUI palette and native Windows caption buttons.</summary>
public class SpaceForm : Form
{
    private static readonly ConditionalWeakTable<Control, object> themedControls = new();
    private readonly Font themeFont = new("Segoe UI", 9.5f);
    public SpaceForm()
    {
        Font = themeFont;
        BackColor = SpaceTheme.Background;
        ForeColor = SpaceTheme.Text;
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowColors();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control != null) AttachTheme(e.Control);
    }

    private static void AttachTheme(Control control)
    {
        // Cached pages are detached and reparented during navigation. Their child-added
        // subscriptions remain active, so already attached subtrees need no second walk.
        if (themedControls.TryGetValue(control, out _)) return;
        try
        {
            SpaceTheme.StyleControl(control);
            themedControls.Add(control, new object());
            control.ControlAdded += ChildAdded;
            foreach (Control child in control.Controls) AttachTheme(child);
        }
        catch
        {
            control.ControlAdded -= ChildAdded;
            themedControls.Remove(control);
            throw;
        }
    }

    private static void ChildAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control != null) AttachTheme(e.Control);
    }

    private void ApplyWindowColors()
    {
        // DWM preserves resizing, snapping, system menus and accessible caption buttons.
        // Older Windows releases ignore unsupported caption-color attributes.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return;
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        SetColor(34, SpaceTheme.Border);
        SetColor(35, SpaceTheme.Surface);
        SetColor(36, SpaceTheme.Text);
    }

    private void SetColor(int attribute, Color color)
    {
        int colorRef = color.R | (color.G << 8) | (color.B << 16);
        DwmSetWindowAttribute(Handle, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) themeFont.Dispose();
    }
}

public class SpaceListView : ListView
{
    public SpaceListView()
    {
        BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text;
        BorderStyle = BorderStyle.FixedSingle; FullRowSelect = true;
        HideSelection = false; DoubleBuffered = true;
    }
}

public class SpaceTreeView : TreeView
{
    public SpaceTreeView()
    {
        BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text;
        LineColor = SpaceTheme.Border; BorderStyle = BorderStyle.FixedSingle; HideSelection = false;
    }
}

public class SpaceListBox : ListBox
{
    public SpaceListBox() { BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text; BorderStyle = BorderStyle.FixedSingle; }
}

public class SpaceNumericUpDown : NumericUpDown
{
    public SpaceNumericUpDown() { BackColor = SpaceTheme.Background; ForeColor = SpaceTheme.Text; BorderStyle = BorderStyle.FixedSingle; }
}

public class SpaceDataGridView : DataGridView
{
    public SpaceDataGridView() { DoubleBuffered = true; SpaceTheme.StyleGrid(this); }
}

public class SpaceMenuStrip : MenuStrip
{
    public SpaceMenuStrip() { Renderer = new SpaceToolStripRenderer(); BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text; }
}

public class SpaceContextMenuStrip : ContextMenuStrip
{
    public SpaceContextMenuStrip() { Renderer = new SpaceToolStripRenderer(); BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text; }
    public SpaceContextMenuStrip(IContainer container) : this() { container.Add(this); }
}

public class SpaceToolStrip : ToolStrip
{
    public SpaceToolStrip() { Renderer = new SpaceToolStripRenderer(); BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text; GripStyle = ToolStripGripStyle.Hidden; }
}

public class SpaceStatusStrip : StatusStrip
{
    public SpaceStatusStrip() { Renderer = new SpaceToolStripRenderer(); BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text; }
}

/// <summary>One renderer for menu bars, toolbars, dropdowns and context menus. All fills are solid.</summary>
public class SpaceToolStripRenderer : ToolStripProfessionalRenderer
{
    public SpaceToolStripRenderer() : base(new SpaceColorTable()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? SpaceTheme.Text : SpaceTheme.Disabled;
        base.OnRenderItemText(e);
    }
    private sealed class SpaceColorTable : ProfessionalColorTable
    {
        public SpaceColorTable() { UseSystemColors = false; }
        public override Color ToolStripGradientBegin => SpaceTheme.Surface;
        public override Color ToolStripGradientMiddle => SpaceTheme.Surface;
        public override Color ToolStripGradientEnd => SpaceTheme.Surface;
        public override Color MenuStripGradientBegin => SpaceTheme.Surface;
        public override Color MenuStripGradientEnd => SpaceTheme.Surface;
        public override Color StatusStripGradientBegin => SpaceTheme.Surface;
        public override Color StatusStripGradientEnd => SpaceTheme.Surface;
        public override Color ToolStripDropDownBackground => SpaceTheme.Surface;
        public override Color ImageMarginGradientBegin => SpaceTheme.Surface;
        public override Color ImageMarginGradientMiddle => SpaceTheme.Surface;
        public override Color ImageMarginGradientEnd => SpaceTheme.Surface;
        public override Color MenuItemSelected => SpaceTheme.Elevated;
        public override Color MenuItemSelectedGradientBegin => SpaceTheme.Elevated;
        public override Color MenuItemSelectedGradientEnd => SpaceTheme.Elevated;
        public override Color MenuItemPressedGradientBegin => SpaceTheme.Elevated;
        public override Color MenuItemPressedGradientMiddle => SpaceTheme.Elevated;
        public override Color MenuItemPressedGradientEnd => SpaceTheme.Elevated;
        public override Color ButtonSelectedGradientBegin => SpaceTheme.Elevated;
        public override Color ButtonSelectedGradientMiddle => SpaceTheme.Elevated;
        public override Color ButtonSelectedGradientEnd => SpaceTheme.Elevated;
        public override Color ButtonPressedGradientBegin => SpaceTheme.PrimaryDark;
        public override Color ButtonPressedGradientMiddle => SpaceTheme.PrimaryDark;
        public override Color ButtonPressedGradientEnd => SpaceTheme.PrimaryDark;
        public override Color ButtonCheckedGradientBegin => SpaceTheme.Elevated;
        public override Color ButtonCheckedGradientMiddle => SpaceTheme.Elevated;
        public override Color ButtonCheckedGradientEnd => SpaceTheme.Elevated;
        public override Color CheckBackground => SpaceTheme.Elevated;
        public override Color CheckSelectedBackground => SpaceTheme.PrimaryDark;
        public override Color CheckPressedBackground => SpaceTheme.PrimaryDark;
        public override Color MenuBorder => SpaceTheme.Border;
        public override Color MenuItemBorder => SpaceTheme.Border;
        public override Color ToolStripBorder => SpaceTheme.Border;
        public override Color SeparatorDark => SpaceTheme.Border;
        public override Color SeparatorLight => SpaceTheme.Border;
    }
}

/// <summary>Native tab navigation and pages with a flat, buffered header and page surround.</summary>
public class SpaceTabControl : TabControl
{
    public SpaceTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        ForeColor = SpaceTheme.Text;
        Padding = new Point(12, 6);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TabPage page) { page.UseVisualStyleBackColor = false; page.BackColor = SpaceTheme.Surface; page.ForeColor = SpaceTheme.Text; }
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count) return;
        bool selected = e.Index == SelectedIndex;
        using var brush = new SolidBrush(selected ? SpaceTheme.PrimaryDark : SpaceTheme.Surface);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, e.Bounds,
            selected ? SpaceTheme.Background : SpaceTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && selected && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -3, -3));
        base.OnDrawItem(e);
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(SpaceTheme.Surface);
    protected override void OnPaint(PaintEventArgs e)
    {
        for (int index = 0; index < TabCount; index++)
        {
            var bounds = GetTabRect(index);
            if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
            var item = new DrawItemEventArgs(e.Graphics, Font, bounds, index,
                index == SelectedIndex ? DrawItemState.Selected : DrawItemState.None);
            OnDrawItem(item);
        }
        base.OnPaint(e);
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
}

public class SpaceGroupBox : GroupBox
{
    public SpaceGroupBox() { BackColor = SpaceTheme.Surface; ForeColor = SpaceTheme.Text; FlatStyle = FlatStyle.Flat; }
}
