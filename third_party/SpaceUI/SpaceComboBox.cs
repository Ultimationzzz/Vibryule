using System.ComponentModel;

namespace SpaceUI;

/// <summary>Selection-only ComboBox with flat chrome and a matching popup. Native item/binding API is retained.</summary>
public class SpaceComboBox : ComboBox
{
    private ToolStripDropDown? popup;
    private ChoiceList? choices;
    private bool hover;
    public SpaceComboBox()
    {
        base.DropDownStyle = ComboBoxStyle.DropDownList;
        BackColor = SpaceTheme.Elevated; ForeColor = SpaceTheme.Text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    [DefaultValue(ComboBoxStyle.DropDownList)]
    public new ComboBoxStyle DropDownStyle
    {
        get => base.DropDownStyle;
        set { if (value != ComboBoxStyle.DropDownList) throw new NotSupportedException("SpaceComboBox supports selection-only dropdowns."); base.DropDownStyle = value; }
    }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool DroppedDown { get => popup?.Visible == true; set { if (value) OpenPopup(); else popup?.Close(); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var border = new Pen(Focused || DroppedDown ? SpaceTheme.Primary : hover ? SpaceTheme.Disabled : SpaceTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        int pad = LogicalToDeviceUnits(10), arrow = LogicalToDeviceUnits(32);
        var bounds = new Rectangle(pad, 0, Math.Max(0, Width - arrow - pad), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? ForeColor : SpaceTheme.Disabled,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        int x = Width - arrow / 2, y = Height / 2;
        using var pen = new Pen(Enabled ? SpaceTheme.Muted : SpaceTheme.Disabled, 1.5f);
        e.Graphics.DrawLines(pen, [new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2)]);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { Focus(); DroppedDown = !DroppedDown; }
        // Do not invoke native ComboBox mouse handling, which would open its unstyled popup.
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) popup?.Close(); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.F4 or Keys.Space || (e.Alt && e.KeyCode == Keys.Down))
        { DroppedDown = !DroppedDown; e.Handled = e.SuppressKeyPress = true; return; }
        base.OnKeyDown(e);
    }
    protected override void WndProc(ref Message m)
    {
        // Swallow native mouse messages before COMBOBOX can create its system list window.
        if (m.Msg is 0x201 or 0x203)
        {
            if (Enabled) { Focus(); DroppedDown = !DroppedDown; }
            return;
        }
        if (m.Msg == 0x202) return;
        // CB_SHOWDROPDOWN can be sent by accessibility clients or native keyboard handling.
        if (m.Msg == 0x14F) { DroppedDown = m.WParam != IntPtr.Zero; return; }
        base.WndProc(ref m);
    }
    private void OpenPopup()
    {
        if (!Enabled || Items.Count == 0 || DroppedDown) return;
        popup?.Dispose();
        choices = new ChoiceList(this) { Font = Font };
        int rowHeight = Math.Max(LogicalToDeviceUnits(30), Font.Height + LogicalToDeviceUnits(12));
        choices.RowHeight = rowHeight;
        choices.Size = new Size(Math.Max(Width, DropDownWidth), Math.Min(Math.Max(1, MaxDropDownItems), Items.Count) * rowHeight);
        var host = new ToolStripControlHost(choices) { AutoSize = false, Size = choices.Size, Margin = Padding.Empty, Padding = Padding.Empty };
        popup = new ToolStripDropDown { AutoSize = true, Padding = new Padding(1), BackColor = SpaceTheme.Border, DropShadowEnabled = false };
        popup.Items.Add(host);
        popup.Closed += (_, _) => { Invalidate(); OnDropDownClosed(EventArgs.Empty); };
        OnDropDown(EventArgs.Empty);
        popup.Show(this, new Point(0, Height)); choices.Focus(); Invalidate();
    }
    protected override void Dispose(bool disposing) { if (disposing) popup?.Dispose(); base.Dispose(disposing); }

    private sealed class ChoiceList : Control
    {
        private readonly SpaceComboBox owner;
        private readonly SpaceScrollBar bar = new() { Dock = DockStyle.Right, Width = 14, TabStop = false };
        private int active;
        [DefaultValue(30)] public int RowHeight { get; set; } = 30;
        public ChoiceList(SpaceComboBox owner)
        {
            this.owner = owner; active = Math.Max(0, owner.SelectedIndex);
            BackColor = SpaceTheme.Elevated; ForeColor = SpaceTheme.Text;
            SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Controls.Add(bar); bar.ValueChanged += (_, _) => Invalidate();
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e); if (bar == null) return;
            bar.Maximum = Math.Max(0, owner.Items.Count * RowHeight - Height); bar.ViewportSize = Height; bar.Visible = bar.Maximum > 0; EnsureVisible();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            int width = Width - (bar.Visible ? bar.Width : 0);
            int first = bar.Value / RowHeight;
            int last = Math.Min(owner.Items.Count - 1, (bar.Value + Height) / RowHeight);
            for (int i = first; i <= last; i++)
            {
                var rect = new Rectangle(0, i * RowHeight - bar.Value, width, RowHeight);
                if (i == active) { using var b = new SolidBrush(SpaceTheme.Elevated); e.Graphics.FillRectangle(b, rect); }
                rect.Inflate(-10, 0);
                TextRenderer.DrawText(e.Graphics, owner.GetItemText(owner.Items[i]), Font, rect, i == owner.SelectedIndex ? SpaceTheme.Accent : ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
        private void SetActive(int index)
        {
            index = Math.Clamp(index, 0, owner.Items.Count - 1); if (active == index) return;
            int old = active; active = index;
            Invalidate(new Rectangle(0, old * RowHeight - bar.Value, Width, RowHeight));
            Invalidate(new Rectangle(0, active * RowHeight - bar.Value, Width, RowHeight));
        }
        private void EnsureVisible()
        {
            if (active * RowHeight < bar.Value) bar.Value = active * RowHeight;
            else if ((active + 1) * RowHeight > bar.Value + Height) bar.Value = (active + 1) * RowHeight - Height;
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); SetActive((e.Y + bar.Value) / RowHeight); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) { SetActive((e.Y + bar.Value) / RowHeight); Commit(); } }
        private void Commit() { owner.SelectedIndex = active; owner.OnSelectionChangeCommitted(EventArgs.Empty); owner.popup?.Close(); owner.Focus(); }
        protected override bool IsInputKey(Keys keyData) => true;
        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up: SetActive(active - 1); break;
                case Keys.Down: SetActive(active + 1); break;
                case Keys.Home: SetActive(0); break;
                case Keys.End: SetActive(owner.Items.Count - 1); break;
                case Keys.PageUp: SetActive(active - Math.Max(1, Height / RowHeight)); break;
                case Keys.PageDown: SetActive(active + Math.Max(1, Height / RowHeight)); break;
                case Keys.Enter: Commit(); break;
                case Keys.Escape: owner.popup?.Close(); owner.Focus(); break;
                default: base.OnKeyDown(e); return;
            }
            EnsureVisible(); e.Handled = e.SuppressKeyPress = true;
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            for (int offset = 1; offset <= owner.Items.Count; offset++)
            {
                int index = (active + offset) % owner.Items.Count;
                if ((owner.GetItemText(owner.Items[index]) ?? string.Empty).StartsWith(e.KeyChar.ToString(), StringComparison.CurrentCultureIgnoreCase)) { SetActive(index); EnsureVisible(); break; }
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { bar.Value -= e.Delta / 120 * RowHeight * 3; base.OnMouseWheel(e); }
    }
}
