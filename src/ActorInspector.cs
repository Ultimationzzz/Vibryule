using System.Text.Json;
using SpaceUI;

namespace OotSceneViewer;

sealed class ActorInspector : UserControl
{
    private readonly SpaceTabControl tabs = new() { Dock = DockStyle.Fill, Multiline = true, Padding = new Point(6, 5) };
    private readonly SpaceTreeView placement = Tree(), types = Tree();
    private readonly SpaceTreeView trigger = Tree();
    private readonly SpaceDataGridView flags = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    private readonly SpaceListBox links = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly SpaceTextBox raw = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9) };
    private readonly Label title = new() { Dock = DockStyle.Top, Height = 44, ForeColor = SpaceTheme.Text, AutoEllipsis = true };
    internal event Action<Actor>? Navigate;
    internal IReadOnlyList<FlagField> FlagFields { get; private set; } = [];
    internal ActorInspector()
    {
        BackColor = SpaceTheme.Surface;
        void Page(string name, Control control)
        {
            var page = new TabPage(name) { BackColor = SpaceTheme.Surface, Padding = new Padding(4) };
            page.Controls.Add(new SpaceScrollHost(control));
            tabs.TabPages.Add(page);
        }
        Page("Fields", placement);
        Page("Flags", flags);
        Page("Type arrays", types);
        Page("Links", links);
        Page("Raw", raw);
        Page("Trigger", trigger);
        trigger.NodeMouseDoubleClick += (_, e) => { if (e.Node?.Tag is Actor actor) Navigate?.Invoke(actor); };
        flags.Columns.Add("Name", "Name");
        flags.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Bool", HeaderText = "Bool", ThreeState = true, ReadOnly = true, ValueType = typeof(CheckState) });
        flags.Columns.Add("Value", "Stored value");
        flags.Columns.Add("Default", "Type default");
        flags.Columns.Add("Source", "Source");
        flags.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        var hint = new Label { Dock = DockStyle.Bottom, Height = 34, ForeColor = SpaceTheme.Muted, Text = "Read-only resource values. Live flag state is unknown.\nDouble-click a resolved link to select its placement." };
        Controls.Add(tabs);
        Controls.Add(hint);
        Controls.Add(title);
        links.DoubleClick += (_, _) => { if (links.SelectedItem is ActorRelationship { Target: not null } link) Navigate?.Invoke(link.Target); };
        links.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && links.SelectedItem is ActorRelationship { Target: not null } link) { Navigate?.Invoke(link.Target); e.Handled = true; } };
        Clear();
    }
    private static SpaceTreeView Tree() => new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ShowLines = true, ShowNodeToolTips = true, HideSelection = false };
    internal void Clear()
    {
        title.Text = "Select an object in the viewport or tree.";
        placement.Nodes.Clear();
        types.Nodes.Clear();
        flags.Rows.Clear();
        links.Items.Clear();
        raw.Clear();
        trigger.Nodes.Clear();
        FlagFields = [];
        tabs.TabPages[1].Text = "Flags";
        tabs.TabPages[3].Text = "Links";
    }

    internal void ShowActor(Actor actor, ActorGraph graph)
    {
        trigger.BeginUpdate();
        trigger.Nodes.Clear();
        trigger.Nodes.AddRange(TriggerInfo.Nodes(actor, graph));
        trigger.EndUpdate();
        title.Text = actor.Name + "\n" + (actor.Raw.TryGetProperty("Name", out var name) ? name.GetString() : actor.Source);
        placement.BeginUpdate();
        placement.Nodes.Clear();
        foreach (var property in actor.Raw.EnumerateObject())
            placement.Nodes.Add(ValueNode(property.Name, property.Value));
        foreach (TreeNode node in placement.Nodes)
            if (node.Name is "Dynamic" or "Presence")
                node.Expand();
        placement.EndUpdate();
        types.BeginUpdate();
        types.Nodes.Clear();
        if (graph.Definitions(actor) is JsonElement definitions)
            foreach (var doc in definitions.EnumerateObject())
            {
                string file = Path.GetFileName(doc.Name);
                int dot = file.IndexOf('.');
                if (dot > 0)
                    file = file[..dot];
                var node = ValueNode(file, doc.Value);
                node.ToolTipText = doc.Name;
                types.Nodes.Add(node);
                node.Expand();
                foreach (TreeNode child in node.Nodes)
                    if (child.Nodes.Count > 0)
                        child.Expand();
            }
        if (types.Nodes.Count == 0)
            types.Nodes.Add("No type definitions in this scene package.");
        types.EndUpdate();
        FlagFields = ReadFlags(actor, graph.Definitions(actor));
        flags.Rows.Clear();
        foreach (var field in FlagFields)
            flags.Rows.Add(field.Name, field.Bool is bool b ? (b ? CheckState.Checked : CheckState.Unchecked) : CheckState.Indeterminate, field.Value, field.Default, field.Source);
        tabs.TabPages[1].Text = $"Flags ({FlagFields.Count})";
        links.Items.Clear();
        links.Items.AddRange(graph.Links(actor).Cast<object>().ToArray());
        tabs.TabPages[3].Text = $"Links ({links.Items.Count})";
        raw.Text = JsonSerializer.Serialize(actor.Raw, new JsonSerializerOptions { WriteIndented = true });
    }
    internal void ShowTriggerTab() => tabs.SelectedIndex = 5;
    internal static TreeNode ValueNode(string key, JsonElement value)
    {
        string summary = value.ValueKind switch
        {
            JsonValueKind.Object => $"{{{value.EnumerateObject().Count()} fields}}",
            JsonValueKind.Array => $"[{value.GetArrayLength()} items]",
            JsonValueKind.True => "☑ true",
            JsonValueKind.False => "☐ false",
            JsonValueKind.String => "\"" + value.GetString() + "\"",
            _ => value.GetRawText()
        };
        var node = new TreeNode(key + "  ·  " + summary) { Name = key, ToolTipText = key + "\n" + value.ValueKind + "\n" + value.GetRawText() };
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var p in value.EnumerateObject())
                node.Nodes.Add(ValueNode(p.Name, p.Value));
        if (value.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (var item in value.EnumerateArray())
            {
                string label = $"[{i++}]";
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("BBKey", out var bb))
                    label += " " + bb.GetString();
                node.Nodes.Add(ValueNode(label, item));
            }
        }
        return node;
    }
    internal static List<FlagField> ReadFlags(Actor actor, JsonElement? definitions)
    {
        var result = new List<FlagField>();
        var overridden = new HashSet<string>(StringComparer.Ordinal);
        bool Named(string key) => key.Contains("Flag", StringComparison.OrdinalIgnoreCase) || key.Contains("GameData", StringComparison.OrdinalIgnoreCase);
        bool IsBool(JsonElement v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False;
        string Display(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrEmpty(v.GetString()) ? "(empty)" : v.GetString()!,
            JsonValueKind.Array => $"[{v.GetArrayLength()} items]",
            JsonValueKind.Object => $"{{{v.EnumerateObject().Count()} fields}}",
            _ => v.GetRawText()
        };
        FlagField Field(string key, JsonElement value, string defaultValue, string source) => new(key, IsBool(value) ? value.GetBoolean() : null, Display(value), defaultValue, source);
        if (definitions is JsonElement docs)
            foreach (var doc in docs.EnumerateObject())
            {
                if (!doc.Name.Contains("BlackboardParamTable/") || doc.Value.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var array in doc.Value.EnumerateObject())
                    if (array.Value.ValueKind == JsonValueKind.Array)
                        foreach (var param in array.Value.EnumerateArray())
                        {
                            if (param.ValueKind != JsonValueKind.Object || !param.TryGetProperty("BBKey", out var keyValue))
                                continue;
                            string key = keyValue.GetString() ?? "";
                            if (!param.TryGetProperty("InitVal", out var initial) && !param.TryGetProperty("InitValConverted", out initial))
                                continue;
                            if (!IsBool(initial) && !Named(key))
                                continue;
                            var value = initial;
                            string source = "Type default · " + Path.GetFileName(doc.Name).Split('.')[0];
                            if (param.TryGetProperty("IsInstanceParam", out var instance) && instance.ValueKind == JsonValueKind.True && actor.Raw.TryGetProperty("Dynamic", out var dynamic) && dynamic.TryGetProperty(key, out var supplied))
                            {
                                value = supplied;
                                source = "Placement override · Dynamic";
                                overridden.Add(key);
                            }
                            result.Add(Field(key, value, Display(initial), source));
                        }
            }
        void Walk(JsonElement value, string path)
        {
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var p in value.EnumerateObject())
                {
                    if (path == "Dynamic" && overridden.Contains(p.Name))
                        continue;
                    string child = path.Length == 0 ? p.Name : path + "." + p.Name;
                    if (IsBool(p.Value) || Named(p.Name))
                        result.Add(Field(child, p.Value, "—", "Placement"));
                    if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        Walk(p.Value, child);
                }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in value.EnumerateArray())
                {
                    string child = path + $"[{i++}]";
                    if (IsBool(item) || Named(path))
                        result.Add(Field(child, item, "—", "Placement"));
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        Walk(item, child);
                }
            }
        }
        Walk(actor.Raw, "");
        return result;
    }
}
record FlagField(string Name, bool? Bool, string Value, string Default, string Source);
