using SpaceUI;

namespace OotSceneViewer;

static class ScrollChecks
{
    internal static string Run()
    {
        var tree = new SpaceTreeView();
        for (int i = 0; i < 200; i++) tree.Nodes.Add($"Object {i}: " + new string('W', 100));
        Check(tree, () => tree.TopNode?.Index > 0);

        var list = new SpaceListBox { HorizontalScrollbar = true, HorizontalExtent = 1600 };
        for (int i = 0; i < 200; i++) list.Items.Add($"Item {i}: " + new string('W', 100));
        Check(list, () => list.TopIndex > 0);

        var text = new SpaceTextBox { Multiline = true, WordWrap = false, ScrollBars = ScrollBars.Both };
        text.Lines = Enumerable.Range(0, 200).Select(i => $"Line {i}: " + new string('W', 100)).ToArray();
        Check(text, () => text.GetLineFromCharIndex(text.GetCharIndexFromPosition(new Point(2, 2))) > 0);

        var panel = new FlowLayoutPanel { AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = SpaceTheme.Surface };
        for (int i = 0; i < 50; i++) panel.Controls.Add(new Label { Text = $"Filter {i}", Size = new Size(500, 30) });
        Check(panel, () => panel.AutoScrollPosition.Y < 0);

        var grid = new SpaceDataGridView();
        for (int i = 0; i < 5; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = $"Column{i}", Width = 200 });
        for (int i = 0; i < 100; i++) grid.Rows.Add($"Flag {i}", true, "Value", "Default", "Source");
        Check(grid, () => grid.FirstDisplayedScrollingRowIndex > 0);
        return "PASS: themed vertical/horizontal scrollbars move tree, list, text, filters and grid content; native bars remain clipped after resize.";
    }

    private static void Check(Control content, Func<bool> moved)
    {
        Console.WriteLine($"Checking scrollbars: {content.GetType().Name}");
        content.Dock = DockStyle.Fill;
        using var form = new SpaceForm { ClientSize = new Size(320, 200), ShowInTaskbar = false, Opacity = 0, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
        using var host = new SpaceScrollHost(content);
        form.Controls.Add(host);
        form.Show();
        Application.DoEvents();
        var bars = host.Controls.OfType<SpaceScrollBar>().ToArray();
        var vertical = bars.Single(b => b.Orientation == Orientation.Vertical);
        var horizontal = bars.Single(b => b.Orientation == Orientation.Horizontal);
        Require(vertical.Maximum > 0 && horizontal.Maximum > 0, $"{content.GetType().Name}: missing scroll ranges ({vertical.Maximum}, {horizontal.Maximum}).");
        var background = content is DataGridView dataGrid ? dataGrid.BackgroundColor : content.BackColor;
        Require(bars.All(b => b.BackColor == background), "Scrollbar track does not match the content palette.");
        vertical.Value = vertical.Maximum / 2;
        Application.DoEvents();
        Require(moved(), $"{content.GetType().Name}: vertical thumb did not move content.");
        horizontal.Value = horizontal.Maximum / 2;
        Application.DoEvents();
        Require(horizontal.Value > 0, $"{content.GetType().Name}: horizontal position was lost.");
        form.ClientSize = new Size(450, 300);
        Application.DoEvents();
        var viewport = host.Controls.OfType<Panel>().Single();
        if (content is not DataGridView)
            Require(content.ClientSize == viewport.ClientSize, $"{content.GetType().Name}: native bars were not clipped ({content.ClientSize} vs {viewport.ClientSize}).");
        Require(vertical.Value <= vertical.Maximum && horizontal.Value <= horizontal.Maximum, "Resize left a thumb outside its range.");
        form.Close();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
