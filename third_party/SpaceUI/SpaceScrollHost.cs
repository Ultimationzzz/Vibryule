using System.Runtime.InteropServices;

namespace SpaceUI;

/// <summary>
/// Keeps native content, selection and keyboard handling, but clips its system
/// scrollbars outside a viewport and exposes SpaceUI scrollbars in both axes.
/// </summary>
public sealed class SpaceScrollHost : UserControl
{
    private readonly Control content;
    private readonly Panel viewport = new();
    private readonly SpaceScrollBar vertical = new() { SmallChange = 1, AccessibleName = "Scroll vertically" };
    private readonly SpaceScrollBar horizontal = new() { Orientation = Orientation.Horizontal, SmallChange = 16, AccessibleName = "Scroll horizontally" };
    private readonly ScrollObserver observer;
    private bool updating, pending;

    public SpaceScrollHost(Control content)
    {
        this.content = content;
        Dock = content.Dock;
        Margin = content.Margin;
        BackColor = viewport.BackColor = vertical.BackColor = horizontal.BackColor =
            content is DataGridView dataGrid ? dataGrid.BackgroundColor : content.BackColor;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        content.Dock = DockStyle.None;
        content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        content.Location = Point.Empty;
        switch (content)
        {
            case TreeView tree: tree.BorderStyle = BorderStyle.None; break;
            case ListBox list:
                list.BorderStyle = BorderStyle.None;
                list.IntegralHeight = false;
                break;
            case TextBoxBase text: text.BorderStyle = BorderStyle.None; break;
            case DataGridView grid: grid.BorderStyle = BorderStyle.None; break;
        }
        Controls.Add(viewport);
        Controls.Add(vertical);
        Controls.Add(horizontal);
        viewport.Controls.Add(content);
        observer = new ScrollObserver(QueueUpdate);
        content.HandleCreated += ContentHandleCreated;
        content.HandleDestroyed += ContentHandleDestroyed;
        content.Layout += ContentLayout;
        content.TextChanged += ContentChanged;
        if (content.IsHandleCreated) observer.Attach(content.Handle);
        vertical.ValueChanged += (_, _) => ScrollTo(true, vertical.Value);
        horizontal.ValueChanged += (_, _) => ScrollTo(false, horizontal.Value);
        Synchronize();
    }

    private void ContentHandleCreated(object? sender, EventArgs e) { observer.Attach(content.Handle); QueueUpdate(); }
    private void ContentHandleDestroyed(object? sender, EventArgs e) => observer.Detach();
    private void ContentLayout(object? sender, LayoutEventArgs e) => QueueUpdate();
    private void ContentChanged(object? sender, EventArgs e) => QueueUpdate();
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); if (content != null) Synchronize(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); QueueUpdate(); }

    private void QueueUpdate()
    {
        if (pending || updating || IsDisposed || !IsHandleCreated) return;
        pending = true;
        BeginInvoke(() => { pending = false; if (!IsDisposed) Synchronize(); });
    }

    private void Synchronize()
    {
        if (updating || IsDisposed) return;
        updating = true;
        try
        {
            int thickness = Math.Max(14, (int)Math.Round(14 * DeviceDpi / 96f));
            // A scrollbar can cause overflow in the other axis. Settle both axes
            // after resizing, using the native control's actual visible range.
            for (int pass = 0; pass < 4; pass++)
            {
                var v = ReadRange(true);
                var h = ReadRange(false);
                bool showV = v.Maximum > 0, showH = h.Maximum > 0;
                int width = Math.Max(1, ClientSize.Width - (showV ? thickness : 0));
                int height = Math.Max(1, ClientSize.Height - (showH ? thickness : 0));
                viewport.Bounds = new Rectangle(0, 0, width, height);
                vertical.Bounds = new Rectangle(width, 0, thickness, height);
                horizontal.Bounds = new Rectangle(0, height, width, thickness);
                vertical.Visible = showV;
                horizontal.Visible = showH;
                int nativeWidth = Math.Max(0, content.Width - content.ClientSize.Width);
                int nativeHeight = Math.Max(0, content.Height - content.ClientSize.Height);
                if (content is DataGridView grid)
                {
                    nativeWidth += grid.Controls.OfType<VScrollBar>().Where(b => b.Visible).Select(b => b.Width).FirstOrDefault();
                    nativeHeight += grid.Controls.OfType<HScrollBar>().Where(b => b.Visible).Select(b => b.Height).FirstOrDefault();
                }
                var size = new Size(width + nativeWidth, height + nativeHeight);
                if (content.Size == size) break;
                content.Size = size;
            }
            ApplyRange(vertical, ReadRange(true));
            ApplyRange(horizontal, ReadRange(false));
        }
        finally { updating = false; }
    }

    private static void ApplyRange(SpaceScrollBar bar, ScrollRange range)
    {
        bar.ViewportSize = range.Page;
        bar.Maximum = range.Maximum;
        bar.Value = range.Position;
    }

    private ScrollRange ReadRange(bool verticalAxis)
    {
        if (!content.IsHandleCreated) return new(0, 1, 0);
        if (content is DataGridView grid)
        {
            ScrollBar? bar = verticalAxis ? grid.Controls.OfType<VScrollBar>().FirstOrDefault() : grid.Controls.OfType<HScrollBar>().FirstOrDefault();
            return bar is { Visible: true } ? new(Math.Max(0, bar.Maximum - bar.LargeChange + 1), Math.Max(1, bar.LargeChange), bar.Value) : new(0, 1, 0);
        }
        var info = new ScrollInfo { Size = Marshal.SizeOf<ScrollInfo>(), Mask = 7 };
        if (!GetScrollInfo(content.Handle, verticalAxis ? 1 : 0, ref info)) return new(0, 1, 0);
        return new(Math.Max(0, info.Maximum - info.Minimum - Math.Max(0, (int)info.Page - 1)), Math.Max(1, (int)info.Page), info.Position - info.Minimum);
    }

    private void ScrollTo(bool verticalAxis, int value)
    {
        if (updating || !content.IsHandleCreated) return;
        if (content is DataGridView grid)
        {
            if (!verticalAxis) grid.HorizontalScrollingOffset = value;
            else
            {
                int offset = 0;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (!row.Visible || row.Frozen) continue;
                    if (offset + row.Height > value) { grid.FirstDisplayedScrollingRowIndex = row.Index; break; }
                    offset += row.Height;
                }
            }
        }
        else if (content is ScrollableControl panel)
        {
            var position = panel.AutoScrollPosition;
            panel.AutoScrollPosition = verticalAxis ? new Point(-position.X, value) : new Point(value, -position.Y);
        }
        else if (verticalAxis && content is ListBox list) list.TopIndex = value;
        else if (verticalAxis && content is TreeView tree)
        {
            int delta = value - ReadRange(true).Position;
            var node = tree.TopNode;
            while (delta != 0 && node != null)
            {
                var next = delta > 0 ? node.NextVisibleNode : node.PrevVisibleNode;
                if (next == null) break;
                node = next;
                delta += delta > 0 ? -1 : 1;
            }
            if (node != null) tree.TopNode = node;
        }
        else if (verticalAxis && content is TextBoxBase)
            SendMessage(content.Handle, 0x00B6, IntPtr.Zero, (IntPtr)(value - ReadRange(true).Position)); // EM_LINESCROLL
        else
            SendMessage(content.Handle, verticalAxis ? 0x0115 : 0x0114, (IntPtr)((Math.Min(value, ushort.MaxValue) << 16) | 4), IntPtr.Zero); // SB_THUMBPOSITION
        Synchronize();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            content.HandleCreated -= ContentHandleCreated;
            content.HandleDestroyed -= ContentHandleDestroyed;
            content.Layout -= ContentLayout;
            content.TextChanged -= ContentChanged;
            observer.Detach();
        }
        base.Dispose(disposing);
    }

    private readonly record struct ScrollRange(int Maximum, int Page, int Position);
    [StructLayout(LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public int Size, Mask, Minimum, Maximum;
        public uint Page;
        public int Position, TrackPosition;
    }
    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr window, int bar, ref ScrollInfo info);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private sealed class ScrollObserver(Action changed) : NativeWindow
    {
        internal void Attach(IntPtr handle) { if (Handle != IntPtr.Zero) ReleaseHandle(); AssignHandle(handle); }
        internal void Detach() { if (Handle != IntPtr.Zero) ReleaseHandle(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is 0x0005 or 0x000F or 0x0085 or 0x0101 or 0x0114 or 0x0115 or 0x020A or 0x020E)
                changed();
        }
    }
}
