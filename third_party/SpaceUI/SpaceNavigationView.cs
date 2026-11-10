using System.ComponentModel;

namespace SpaceUI;

/// <summary>Grouped, searchable navigation with lazy cached pages. Only the active page participates in layout.</summary>
[DefaultEvent(nameof(SelectedPageChanged))]
public class SpaceNavigationView : UserControl
{
    private readonly Panel sidebar = new()
    {
        Dock = DockStyle.Left,
        Width = 168,
        AutoScroll = true,
        BackColor = SpaceTheme.Surface,
    };
    private readonly Panel content = new()
    {
        Dock = DockStyle.Fill,
        BackColor = SpaceTheme.Background,
    };
    private readonly Dictionary<string, PageEntry> pages = new(StringComparer.Ordinal);
    private readonly List<string> order = [];
    private readonly Dictionary<string, Label> groups = new(StringComparer.Ordinal);
    private Font? hierarchyFont;
    private Font? hierarchyFontSource;
    private string selectedKey = string.Empty;
    private bool selecting,
        arranging,
        filtered;
    private int updateDepth;

    public event EventHandler? SelectedPageChanged;

    [DefaultValue(168)]
    public int SidebarWidth
    {
        get => sidebar.Width;
        set
        {
            if (value < 80)
                throw new ArgumentOutOfRangeException(nameof(value));
            if (sidebar.Width != value)
                sidebar.Width = value;
        }
    }

    [DefaultValue("")]
    public string SelectedKey
    {
        get => selectedKey;
        set => SelectPage(value);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? SelectedPage => selectedKey.Length == 0 ? null : pages[selectedKey].Content;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Panel ContentHost => content;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Panel Navigation => sidebar;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IEnumerable<string> VisiblePageKeys => order.Where(key => pages[key].Visible);

    public SpaceNavigationView()
    {
        BackColor = SpaceTheme.Background;
        Controls.Add(content);
        Controls.Add(sidebar);
        sidebar.SizeChanged += (_, _) => ArrangeButtons();
    }

    /// <summary>Batch registration so the sidebar is laid out once, without creating any pages.</summary>
    public void BeginUpdate() => updateDepth++;

    public void EndUpdate()
    {
        if (updateDepth == 0)
            throw new InvalidOperationException("BeginUpdate must precede EndUpdate.");
        if (--updateDepth == 0)
            ArrangeButtons();
    }

    /// <summary>Registers a lazy page and optional sidebar group/indent. The view owns returned pages.</summary>
    public void AddPage(
        string key,
        string title,
        Func<Control> factory,
        string group = "",
        int indent = 0
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(group);
        if (indent < 0 || indent > 8)
            throw new ArgumentOutOfRangeException(nameof(indent));
        if (pages.ContainsKey(key))
            throw new ArgumentException("Page keys must be unique.", nameof(key));
        var button = new SpaceNavigationButton
        {
            Text = title,
            UseMnemonic = false,
            AccessibleName = title,
            TabIndex = order.Count,
            Tag = key,
        };
        button.Click += (_, _) => SelectPage(key);
        pages.Add(key, new PageEntry(factory, button, title, group, indent));
        order.Add(key);
        if (group.Length > 0 && !groups.ContainsKey(group))
        {
            var heading = new SpaceNavigationGroupHeader
            {
                Text = group.ToUpperInvariant(),
                AccessibleName = group,
            };
            groups.Add(group, heading);
            sidebar.Controls.Add(heading);
        }
        sidebar.Controls.Add(button);
        ArrangeButtons();
    }

    public SpaceNavigationButton GetPageButton(string key) => pages[key].Button;

    /// <summary>Filters existing buttons without destroying cached pages. Null restores all groups; null results hide a page.</summary>
    public void FilterPages(Func<string, string?>? match)
    {
        // Evaluate first: a failed predicate must not partially modify the navigation.
        var matches = order
            .Select(key => (Key: key, Description: match == null ? "" : match(key)))
            .ToArray();
        bool nextFiltered = match != null;
        bool changed = filtered != nextFiltered;
        foreach (var (key, description) in matches)
        {
            var page = pages[key];
            changed |=
                page.Visible != (description != null) || page.Description != (description ?? "");
        }
        if (!changed)
            return;
        sidebar.SuspendLayout();
        try
        {
            filtered = nextFiltered;
            foreach (var (key, description) in matches)
            {
                var page = pages[key];
                bool visible = description != null;
                string nextDescription = description ?? "";
                if (page.Description != nextDescription)
                {
                    page.Description = nextDescription;
                    page.Button.Text =
                        nextDescription.Length == 0
                            ? page.Title
                            : page.Title + "\n" + nextDescription;
                }
                if (page.Visible != visible)
                {
                    page.Visible = visible;
                    page.Button.Visible = visible;
                }
            }
            ArrangeButtons();
        }
        finally
        {
            // Settle native AutoScroll after the batch, including a shrunken result set.
            sidebar.ResumeLayout(true);
        }
    }

    /// <summary>Creates before switching so a failed factory leaves the active page intact. Re-selection is a no-op.</summary>
    public void SelectPage(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key == selectedKey)
            return;
        if (!pages.TryGetValue(key, out var next))
            throw new ArgumentException("Unknown page key.", nameof(key));
        if (selecting)
            throw new InvalidOperationException(
                "Navigation cannot change while a page is being created."
            );
        selecting = true;
        try
        {
            if (next.Content == null)
            {
                var created =
                    next.Factory()
                    ?? throw new InvalidOperationException("Page factory returned null.");
                if (
                    created.IsDisposed
                    || created.Parent != null
                    || created is Form
                    || ReferenceEquals(created, this)
                )
                    throw new InvalidOperationException(
                        "A page factory must return an unparented child control, not a Form."
                    );
                next.Content = created;
            }
            content.SuspendLayout();
            try
            {
                if (selectedKey.Length != 0)
                {
                    var previous = pages[selectedKey];
                    previous.Button.Selected = false;
                    previous.Content!.Visible = false;
                    content.Controls.Remove(previous.Content);
                }
                next.Content.Dock = DockStyle.Fill;
                content.Controls.Add(next.Content);
                next.Content.BringToFront();
                next.Content.Visible = true;
                selectedKey = key;
                next.Button.Selected = true;
            }
            finally
            {
                content.ResumeLayout(true);
            }
        }
        finally
        {
            selecting = false;
        }
        SelectedPageChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool RemovePage(string key)
    {
        if (!pages.TryGetValue(key, out var page))
            return false;
        if (selecting)
            throw new InvalidOperationException("Cannot remove pages during navigation.");
        bool wasSelected = selectedKey == key;
        int index = order.IndexOf(key);
        if (wasSelected && order.Count > 1)
            SelectPage(order[index == order.Count - 1 ? index - 1 : index + 1]);
        else if (wasSelected)
        {
            if (page.Content != null)
                content.Controls.Remove(page.Content);
            selectedKey = string.Empty;
        }
        pages.Remove(key);
        order.Remove(key);
        page.Content?.Dispose();
        page.Button.Dispose();
        if (page.Group.Length > 0 && !pages.Values.Any(p => p.Group == page.Group))
        {
            groups[page.Group].Dispose();
            groups.Remove(page.Group);
        }
        ArrangeButtons();
        if (wasSelected && selectedKey.Length == 0)
            SelectedPageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void ArrangeButtons()
    {
        if (arranging || updateDepth > 0)
            return;
        arranging = true;
        sidebar.SuspendLayout();
        try
        {
            UpdateHierarchy();
            int total = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in order)
            {
                var page = pages[key];
                if (!page.Visible)
                    continue;
                if (!filtered && page.Group.Length > 0 && seen.Add(page.Group))
                    total += GroupHeight;
                total += RowHeight(page);
            }
            int width = Math.Max(
                0,
                sidebar.Width
                    - (
                        total > sidebar.ClientSize.Height
                            ? SystemInformation.VerticalScrollBarWidth
                            : 0
                    )
            );
            int y = 0,
                offset = sidebar.AutoScrollPosition.Y;
            seen.Clear();
            var visibleGroups = pages
                .Values.Where(page => page.Visible)
                .Select(page => page.Group)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var (group, heading) in groups)
                heading.Visible = !filtered && visibleGroups.Contains(group);
            foreach (var key in order)
            {
                var page = pages[key];
                if (!page.Visible)
                    continue;
                if (!filtered && page.Group.Length > 0 && seen.Add(page.Group))
                {
                    var heading = groups[page.Group];
                    int height = GroupHeight;
                    heading.SetBounds(0, y + offset, width, height);
                    heading.Padding = new(
                        LogicalToDeviceUnits(16),
                        LogicalToDeviceUnits(12),
                        LogicalToDeviceUnits(8),
                        0
                    );
                    y += height;
                }
                int rowHeight = RowHeight(page);
                page.Button.Padding = new(
                    LogicalToDeviceUnits(16 + (filtered ? 0 : page.Indent * 20)),
                    0,
                    LogicalToDeviceUnits(8),
                    0
                );
                page.Button.SetBounds(0, y + offset, width, rowHeight);
                y += rowHeight;
            }
            sidebar.AutoScrollMinSize = new(0, total);
        }
        finally
        {
            sidebar.ResumeLayout(false);
            arranging = false;
        }
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ArrangeButtons();
    }

    private int GroupHeight =>
        Math.Max(LogicalToDeviceUnits(42), hierarchyFont!.Height + LogicalToDeviceUnits(24));

    private int RowHeight(PageEntry page)
    {
        bool description = page.Description.Length != 0;
        return Math.Max(
            LogicalToDeviceUnits(description ? 60 : 36),
            page.Button.Font.Height * (description ? 2 : 1)
                + LogicalToDeviceUnits(description ? 18 : 12)
        );
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (pages != null)
            ArrangeButtons();
    }

    private void UpdateHierarchy()
    {
        Font? previous = null;
        if (hierarchyFont == null || !Font.Equals(hierarchyFontSource))
        {
            previous = hierarchyFont;
            hierarchyFontSource = Font;
            hierarchyFont = new Font(Font, Font.Style | FontStyle.Bold);
        }
        foreach (var heading in groups.Values)
            heading.Font = hierarchyFont;
        int followingLevels = 0;
        string? group = null;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var page = pages[order[i]];
            int level = filtered ? 0 : page.Indent;
            // Hidden cached buttons must release an old owned font before it is disposed.
            page.Button.Font = !filtered && level == 0 ? hierarchyFont : Font;
            if (!page.Visible)
                continue;
            if (page.Group != group)
            {
                followingLevels = 0;
                group = page.Group;
            }
            int levelBit = 1 << level;
            page.Button.SetHierarchy(
                level,
                followingLevels & (levelBit - 1),
                (followingLevels & levelBit) == 0
            );
            followingLevels = (followingLevels & (levelBit - 1)) | levelBit;
        }
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var page in pages.Values)
                page.Content?.Dispose();
            pages.Clear();
            order.Clear();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            hierarchyFont?.Dispose();
            hierarchyFont = null;
        }
    }

    private sealed class PageEntry(
        Func<Control> factory,
        SpaceNavigationButton button,
        string title,
        string group,
        int indent
    )
    {
        public Func<Control> Factory { get; } = factory;
        public SpaceNavigationButton Button { get; } = button;
        public string Title { get; } = title;
        public string Group { get; } = group;
        public int Indent { get; } = indent;
        public string Description { get; set; } = "";
        public bool Visible { get; set; } = true;
        public Control? Content { get; set; }
    }
}
