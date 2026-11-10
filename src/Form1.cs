using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SpaceUI;

namespace OotSceneViewer;

public partial class Form1 : SpaceForm
{
    private readonly SceneView view = new() { Dock = DockStyle.Fill };
    private readonly SpaceTreeView tree = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ShowLines = true };
    private readonly SpaceSearchBox filter = new() { Dock = DockStyle.Fill, PlaceholderText = "Search objects or types" };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SpaceTheme.Muted };
    private readonly ActorInspector details = new() { Dock = DockStyle.Fill };
    private readonly SpaceTabControl sidebarTabs = new() { Dock = DockStyle.Fill };
    private readonly Label counts = new() { AutoSize = false, Dock = DockStyle.Fill, ForeColor = SpaceTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Dictionary<Actor, TreeNode> actorNodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, SpaceCheckBox> categoryControls = [];
    private readonly SpaceToggle markers = Toggle("Placement markers", false);
    private readonly SpaceToggle candidates = Toggle("Area / event candidates", false);
    private readonly SpaceToggle areaExtents = Toggle("Zone volumes", false);
    private readonly SpaceToggle gameRendering = Toggle("Original textures", true);
    private readonly SpaceToggle terrain = Toggle("Terrain surface", true);
    private readonly SpaceSearchBox extentFilter = new() { Width = 320, Height = 32, PlaceholderText = "Extent names: Area, Tree, placement name…" };
    private readonly SpaceToggle filterModels = Toggle("Also filter models", false);
    private readonly SpaceToggle pickExtents = Toggle("Pick extents instead of surfaces", false);
    private readonly Label extentMatches = new() { Width = 320, Height = 24, ForeColor = SpaceTheme.Muted };
    private readonly SpaceToggle selectedLinks = Toggle("Selected placement links", true);
    private readonly SpaceToggle bounds = Toggle("Model bounds", false);
    private readonly SelectionModeButton mapObjectSelection = new(false);
    private readonly SelectionModeButton mapZoneSelection = new(true);
    private readonly CancellationTokenSource workflowCancellation = new();
    private readonly NupWorkflow nupWorkflow = new();
    private string? loadedNupPath;
    private List<SceneOption> availableScenes = [];
    private SceneOption? currentNupScene;
    private bool workflowBusy;
    private List<Actor> actors = [];
    private ActorGraph graph = new([]);
    private readonly SplitContainer objectSplit = new() { Dock = DockStyle.Fill, Size = new Size(1600, 800), SplitterWidth = 8, SplitterDistance = 300, Panel1MinSize = 220, Panel2MinSize = 700, FixedPanel = FixedPanel.Panel1, BackColor = SpaceTheme.Border };
    private readonly SplitContainer inspectorSplit = new() { Dock = DockStyle.Fill, Size = new Size(1292, 800), SplitterWidth = 8, SplitterDistance = 884, Panel1MinSize = 300, Panel2MinSize = 380, FixedPanel = FixedPanel.Panel2, BackColor = SpaceTheme.Border };

    public Form1(string[]? initialFiles = null)
    {
        InitializeComponent();
        using (var iconStream = typeof(Form1).Assembly.GetManifestResourceStream("Vibryule.AppIcon"))
            if (iconStream != null)
            {
                using var loadedIcon = new Icon(iconStream);
                var appIcon = (Icon)loadedIcon.Clone();
                Icon = appIcon;
                Disposed += (_, _) => appIcon.Dispose();
            }
        Text = "Vibryule";
        ClientSize = new Size(1650, 950);
        MinimumSize = new Size(1250, 720);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), BackColor = SpaceTheme.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);
        MainMenuStrip = BuildWindowMenu();
        root.Controls.Add(MainMenuStrip, 0, 0);
        root.Controls.Add(objectSplit, 0, 1);
        objectSplit.Panel2.Controls.Add(inspectorSplit);
        var resizeHints = new ToolTip(components!);
        resizeHints.SetToolTip(objectSplit, "Drag to resize the object panel. Double-click to reset.");
        resizeHints.SetToolTip(inspectorSplit, "Drag to resize the inspector panel. Double-click to reset.");
        objectSplit.MouseDoubleClick += (_, _) => objectSplit.SplitterDistance = 300;
        inspectorSplit.MouseDoubleClick += (_, _) => inspectorSplit.SplitterDistance = Math.Max(inspectorSplit.Panel1MinSize, inspectorSplit.Width - inspectorSplit.SplitterWidth - 400);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = SpaceTheme.Surface, ColumnCount = 1, RowCount = 4, Padding = new Padding(12), Margin = new Padding(0, 0, 8, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        left.Controls.Add(Heading("OBJECTS"), 0, 0);
        left.Controls.Add(filter, 0, 1);
        left.Controls.Add(new SpaceScrollHost(tree), 0, 2);
        left.Controls.Add(counts, 0, 3);
        objectSplit.Panel1.Controls.Add(left);
        var viewport = new Panel { Dock = DockStyle.Fill, BackColor = SpaceTheme.Background };
        var selectionModes = new FlowLayoutPanel
        {
            Location = new Point(10, 10), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(2), Margin = Padding.Empty, WrapContents = false,
            BackColor = SpaceTheme.Surface, BorderStyle = BorderStyle.FixedSingle
        };
        selectionModes.Controls.Add(mapObjectSelection);
        selectionModes.Controls.Add(mapZoneSelection);
        mapObjectSelection.Click += (_, _) => { pickZones.Checked = false; pickExtents.Checked = false; };
        mapZoneSelection.Click += (_, _) => pickZones.Checked = true;
        resizeHints.SetToolTip(mapObjectSelection, "Objects — select model surfaces and placement markers.");
        resizeHints.SetToolTip(mapZoneSelection, "Zones — select the smallest zone under the cursor, even when overlays are hidden. Ctrl+Shift+Z switches modes.");
        viewport.Controls.Add(view);
        viewport.Controls.Add(selectionModes);
        selectionModes.BringToFront();
        inspectorSplit.Panel1.Controls.Add(viewport);
        view.FlySpeedChanged += speed => resizeHints.SetToolTip(mapObjectSelection, $"Objects — select model surfaces and placement markers.\nFly speed: {speed:F1}. Wheel down speeds up; wheel up slows down.");
        var selectionPage = new TabPage("Selection") { BackColor = SpaceTheme.Surface, Padding = new Padding(8) };
        var displayPage = new TabPage("Display & Filters") { BackColor = SpaceTheme.Surface, Padding = new Padding(8) };
        var selectionLayout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = SpaceTheme.Surface, ColumnCount = 1, RowCount = 2 };
        selectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        selectionLayout.Controls.Add(details, 0, 0);
        selectionLayout.Controls.Add(Button("Inspect placement data", 210, (_, _) => InspectSelected()), 0, 1);
        selectionPage.Controls.Add(selectionLayout);
        displayPage.Controls.Add(BuildDisplayOptions());
        sidebarTabs.TabPages.AddRange([selectionPage, displayPage]);
        inspectorSplit.Panel2.Controls.Add(sidebarTabs);
        status.Text = "File → Open NUP to begin. Model bounds and inspection volumes are not verified collisions.";
        root.Controls.Add(status, 0, 2);
        filter.TextChanged += (_, _) => RefreshTree();
        tree.AfterSelect += (_, e) => { if (e.Node?.Tag is Actor actor) SelectActor(actor); };
        tree.NodeMouseDoubleClick += (_, e) => { if (e.Node?.Tag is Actor actor) view.FocusActor(actor); };
        view.ActorPicked += SelectFromViewport;
        details.Navigate += SelectFromViewport;
        UpdateMenuState();
        if (initialFiles is { Length: > 0 })
            Shown += (_, _) => LoadScene(initialFiles);
        else
            Shown += async (_, _) => await RestoreNupAsync();
    }

    private static Label Heading(string text) => new() { Text = text, Dock = DockStyle.Fill, ForeColor = SpaceTheme.Accent, Font = new Font("Segoe UI", 9, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    private static SpaceButton Button(string text, int width, EventHandler click)
    {
        var b = new SpaceButton { Text = text, Width = width, Height = 32, ButtonStyle = SpaceButtonStyle.Secondary, Margin = new Padding(4, 0, 0, 0) };
        b.Click += click;
        return b;
    }
    private static SpaceToggle Toggle(string text, bool value) => new() { Text = text, Checked = value, Width = 220, Height = 30 };



    private void Open()
    {
        using var dialog = new OpenFileDialog { Filter = "Scene package or placement JSON|*.json|All files|*.*", Multiselect = true, Title = "Open a prepared scene.json or placement files" };
        if (dialog.ShowDialog() != DialogResult.OK)
            return;
        if (LoadScene(dialog.FileNames))
            currentNupScene = null;
    }
    private async Task OpenNupAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "NUP game package|*.nup", Title = "Select your local game NUP" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        await OpenNupPathAsync(dialog.FileName, true);
    }
    private async Task RestoreNupAsync()
    {
        try
        {
            var path = ViewerConfig.ReadNupPath(ViewerConfig.DefaultPath);
            if (path == null)
                return;
            if (!File.Exists(path))
            {
                status.Text = "Saved NUP not found. Use Open NUP to select it again.";
                return;
            }
            await OpenNupPathAsync(path, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            status.Text = $"Could not read config.json: {ex.Message}";
        }
    }
    private async Task OpenNupPathAsync(string nupPath, bool saveSelection)
    {
        if (workflowBusy)
            return;
        workflowBusy = true;
        UpdateMenuState();
        try
        {
            var progress = PreparationProgress();
            status.Text = "Indexing your local NUP…";
            var scenes = await nupWorkflow.ListAsync(nupPath, progress, workflowCancellation.Token);
            workflowCancellation.Token.ThrowIfCancellationRequested();
            if (scenes.Count == 0)
                throw new InvalidDataException("No supported scene placement files were found in this NUP.");
            loadedNupPath = Path.GetFullPath(nupPath);
            availableScenes = scenes;
            currentNupScene = null;
            if (saveSelection)
            {
                try
                {
                    ViewerConfig.SaveNupPath(ViewerConfig.DefaultPath, nupPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                {
                    MessageBox.Show(this, $"The NUP opened, but its path could not be saved to config.json: {ex.Message}", "Could not save NUP path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            await ChooseNupSceneAsync(progress);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) { status.Text = "Scene preparation failed."; MessageBox.Show(this, ex.Message, "Could not prepare NUP", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        finally { workflowBusy = false; if (!IsDisposed) UpdateMenuState(); }
    }
    private IProgress<string> PreparationProgress() => new Progress<string>(message => { if (!IsDisposed && workflowBusy) status.Text = $"PREPARING  •  {message}"; });
    private async Task ChangeSceneAsync()
    {
        if (workflowBusy || loadedNupPath == null || availableScenes.Count == 0)
            return;
        workflowBusy = true;
        UpdateMenuState();
        try
        {
            if (!File.Exists(loadedNupPath))
                throw new FileNotFoundException("The loaded NUP is no longer available. Use File → Open NUP to select it again.");
            await ChooseNupSceneAsync(PreparationProgress());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed) { status.Text = "Could not change scene."; MessageBox.Show(this, ex.Message, "Could not change scene", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        finally { workflowBusy = false; if (!IsDisposed) UpdateMenuState(); }
    }
    private async Task ChooseNupSceneAsync(IProgress<string> progress)
    {
        using var picker = new ScenePicker(availableScenes, currentNupScene, Path.GetFileName(loadedNupPath));
        if (picker.ShowDialog(this) != DialogResult.OK || picker.Selection is not SceneOption selected)
        {
            status.Text = $"Scene selection cancelled. {availableScenes.Count} scenes available from Scene → Change Scene.";
            return;
        }
        status.Text = $"Preparing {selected.Name} locally; this may take several minutes…";
        string path = await nupWorkflow.PrepareAsync(loadedNupPath!, selected, progress, workflowCancellation.Token);
        workflowCancellation.Token.ThrowIfCancellationRequested();
        if (!IsDisposed && LoadScene([path]))
            currentNupScene = selected;
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel)
            workflowCancellation.Cancel();
    }
    private bool LoadScene(string[] paths)
    {
        try
        {
            var scene = SceneLoader.ReadScene(paths);
            actors = scene.Actors;
            graph = new ActorGraph(actors, scene.ActorMetadata, scene.LogicGroups);
            view.SetActors(actors, scene.GeometryDirectory, graph);
            PopulateZoneTypes();
            followProbe.Checked = false;
            ResetProbePreview();
            RefreshZones();
            RefreshTree();
            status.Text = $"{scene.Name}  •  {actors.Count:N0} placements  •  {view.ModeledCount:N0} models  •  {view.TerrainTileCount} terrain tiles  •  {view.AreaExtentCount:N0} inspection volumes";
            details.Clear();
            view.SetNameFilter(extentFilter.Text, filterModels.Checked);
            extentMatches.Text = $"{view.NameMatchCount:N0} / {actors.Count:N0} names match · comma = OR";
            Text = $"{scene.Name} — Vibryule";
            UpdateMenuState();
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open scene", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        return false;
    }
    private void RefreshTree()
    {
        tree.BeginUpdate();
        tree.Nodes.Clear();
        actorNodes.Clear();
        var filtered = actors.Where(a => (!treeZonesOnly.Checked || TriggerVolume.IsCandidate(a) || a.Name.Contains("Event", StringComparison.OrdinalIgnoreCase)) && (a.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase) || a.Raw.GetRawText().Contains(filter.Text, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var group in filtered.GroupBy(a => SceneCategories.Get(a.Name)).OrderBy(g => g.Key.Order))
        {
            var root = new TreeNode($"{group.Key.Name}  ({group.Count():N0})");
            tree.Nodes.Add(root);
            foreach (var type in group.GroupBy(a => a.Name).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var node = new TreeNode($"{type.Key}  ({type.Count():N0})");
                root.Nodes.Add(node);
                int index = 0;
                foreach (var actor in type)
                {
                    var instance = new TreeNode($"#{++index}   {actor.Position.X:F1}, {actor.Position.Y:F1}, {actor.Position.Z:F1}") { Tag = actor };
                    node.Nodes.Add(instance);
                    actorNodes[actor] = instance;
                }
            }
            if (!string.IsNullOrWhiteSpace(filter.Text))
                root.Expand();
        }
        counts.Text = $"{filtered.Length:N0} of {actors.Count:N0} placements";
        tree.EndUpdate();
    }
    private void SelectActor(Actor actor)
    {
        view.Selected = actor;
        view.Relationships = graph.Links(actor);
        view.Invalidate();
        details.ShowActor(actor, graph);
        RefreshZones();
        if (view.PickTriggers)
            details.ShowTriggerTab();
    }
    private void SelectFromViewport(Actor? actor)
    {
        if (actor == null)
        {
            view.Selected = null;
            view.Relationships = [];
            tree.SelectedNode = null;
            details.Clear();
            view.Invalidate();
            return;
        }
        if (!actorNodes.ContainsKey(actor))
        {
            treeZonesOnly.Checked = false;
            filter.Text = "";
        }
        SelectActor(actor);
        if (actorNodes.TryGetValue(actor, out var node))
        {
            tree.SelectedNode = node;
            node.EnsureVisible();
        }
    }
    private void InspectSelected()
    {
        if (view.Selected is not Actor actor)
            return;
        using var dialog = new SpaceForm { Text = $"Placement data — {actor.Name}", Width = 1100, Height = 800, StartPosition = FormStartPosition.CenterParent };
        var text = new ActorInspector { Dock = DockStyle.Fill };
        text.ShowActor(actor, graph);
        text.Navigate += target => { SelectFromViewport(target); dialog.Close(); };
        dialog.Controls.Add(text);
        dialog.ShowDialog(this);
    }
}

record SceneInput(string Name, List<Actor> Actors, string? GeometryDirectory, IReadOnlyDictionary<string, JsonElement>? ActorMetadata = null, IReadOnlyList<JsonElement>? LogicGroups = null);
static class SceneLoader
{
    internal static SceneInput ReadScene(string[] paths)
    {
        if (paths.Length == 1)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(paths[0]));
            var root = document.RootElement;
            if (root.TryGetProperty("format", out var format) && format.GetString() == "oot-scene-v1")
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(paths[0]))!;
                string Resolve(string relative)
                {
                    if (Path.IsPathRooted(relative))
                        throw new InvalidDataException("Scene package paths must be relative.");
                    var result = Path.GetFullPath(Path.Combine(directory, relative));
                    var prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
                    if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Scene package path leaves its directory.");
                    return result;
                }
                var files = root.GetProperty("placement_files").EnumerateArray().Select(x => Resolve(x.GetString()!)).ToArray();
                var geometry = Resolve(root.GetProperty("geometry_directory").GetString()!);
                if (!Directory.Exists(geometry))
                    throw new DirectoryNotFoundException($"Scene geometry directory is missing: {geometry}");
                Dictionary<string, JsonElement>? metadata = null;
                if (root.TryGetProperty("actor_metadata", out var metadataPath))
                {
                    using var metadataFile = JsonDocument.Parse(File.ReadAllText(Resolve(metadataPath.GetString()!)));
                    metadata = metadataFile.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
                }
                var logic = new List<JsonElement>();
                return new SceneInput(root.GetProperty("name").GetString() ?? "Scene", ReadActors(files, logic), geometry, metadata, logic);
            }
        }
        var groups = new List<JsonElement>();
        return new SceneInput("Placement files", ReadActors(paths, groups), null, LogicGroups: groups);
    }
    internal static List<Actor> ReadActors(string[] paths, List<JsonElement>? logicGroups = null)
    {
        var loaded = new List<Actor>();
        foreach (var path in paths)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (logicGroups != null && doc.RootElement.TryGetProperty("AiGroups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                logicGroups.AddRange(groups.EnumerateArray().Select(g => g.Clone()));
            foreach (var item in doc.RootElement.GetProperty("Actors").EnumerateArray())
            {
                if (!item.TryGetProperty("Gyaml", out var name) || !item.TryGetProperty("Translate", out var position) || position.GetArrayLength() != 3)
                    continue;
                Vector3 ReadVector(string key, Vector3 fallback) => item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 ? new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : fallback;
                loaded.Add(new Actor(name.GetString() ?? "(unnamed)", new(position[0].GetSingle(), position[1].GetSingle(), position[2].GetSingle()), ReadVector("Rotate", Vector3.Zero), ReadVector("Scale", Vector3.One), Path.GetFileNameWithoutExtension(path), item.Clone()));
            }
        }
        return loaded;
    }
}

record Actor(string Name, Vector3 Position, Vector3 Rotation, Vector3 Scale, string Source, JsonElement Raw)
{
    public override string ToString() => Name;
}

record SceneCategory(string Name, int Mask, int Order);
static class SceneCategories
{
    internal static readonly SceneCategory[] All = [new("Environment", 1, 0), new("Objects", 2, 1), new("Characters", 4, 2), new("Enemies", 8, 3), new("Items & weapons", 16, 4), new("Areas & logic", 32, 5), new("Other", 64, 6)];
    internal static bool IsCandidate(string name) => name.Contains("Area", StringComparison.OrdinalIgnoreCase) || name.Contains("Event", StringComparison.OrdinalIgnoreCase) || name.Contains("Trigger", StringComparison.OrdinalIgnoreCase);
    internal static SceneCategory Get(string name) => name switch
    {
        var n when n.StartsWith("Land_") || n.StartsWith("FldObj_") || n.StartsWith("Tree_") => All[0],
        var n when n.StartsWith("Obj_") => All[1],
        var n when n.StartsWith("Npc_") || n.StartsWith("Player_") || n.StartsWith("Animal_") => All[2],
        var n when n.StartsWith("Enemy_") => All[3],
        var n when n.StartsWith("Item_") || n.StartsWith("Weapon_") || n.StartsWith("Pouch_") => All[4],
        var n when n.Contains("Area") || n.Contains("Anchor") || n.Contains("Event") || n.Contains("Trigger") => All[5],
        _ => All[6]
    };
}

sealed partial class SceneView : Control
{
    private Direct3DRenderer? renderer;
    private List<Actor> actors = [];
    private Vector3 center;
    private float distance = 500, yaw = 0.7f, pitch = 0.8f;
    private float flySpeed = 50;
    internal event Action<float>? FlySpeedChanged;
    private Point previous;
    private Point mouseDown;
    private readonly System.Windows.Forms.Timer movementTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer animationTimer = new() { Interval = 33 };
    private int movementKeys;
    private long previousMovementTick;
    internal event Action<Actor?>? ActorPicked;
    private ScenePicking? picker;
    private bool mouseDragged;
    private string nameFilter = "";
    private HashSet<Actor>? nameMatches;
    internal bool FilterModels, PickExtents;
    internal int NameMatchCount => nameMatches?.Count ?? actors.Count;
    internal bool MatchesName(Actor actor) => nameMatches == null || nameMatches.Contains(actor);
    internal bool ActorVisible(Actor actor) => (CategoryMask & SceneCategories.Get(actor.Name).Mask) != 0 && MatchesName(actor);
    internal bool RangeVisible(SceneRange range) => !(Rendering.Water && range.Water is WaterKind.Volume or WaterKind.Backface) && (CategoryMask & range.Mask) != 0 && (range.Terrain ? ShowTerrain : !FilterModels || range.Owner == null || MatchesName(range.Owner));
    internal void SetNameFilter(string text, bool filterModels)
    {
        nameFilter = text;
        FilterModels = filterModels;
        nameMatches = string.IsNullOrWhiteSpace(text) ? null : new HashSet<Actor>(actors.Where(a => ActorNameFilter.Matches(a, text)), ReferenceEqualityComparer.Instance);
        Invalidate();
    }
    private Dictionary<string, List<ModelShape>> models = [];
    private string? modelDirectory;
    private Vertex[] world = [];
    private Vertex[] boxes = [];
    private SceneRange[] worldRanges = [], boxRanges = [];
    private readonly Dictionary<Actor, (Vector3 Min, Vector3 Max)> actorBounds = new(ReferenceEqualityComparer.Instance);
    internal bool ShowMarkers;
    internal bool ShowCandidates;
    internal bool ShowAreaExtents;
    internal bool GameRendering = true;
    internal bool ShowTerrain = true;
    internal int TerrainTileCount;
    private readonly List<TriggerVolume> areaBoxes = [];
    internal IReadOnlyList<TriggerVolume> Zones => areaBoxes;
    internal readonly RenderSettings Rendering = new();
    internal bool PickTriggers, FillZones = true, ZoneXray = true, ShowApproximate = true, SelectedZonesOnly;
    internal string ZoneSearch = "", ZoneType = "";
    internal bool ShowFallZones = true, ShowItemFallZones = true, ShowFogZones = true, OnlyFallZones;
    internal bool ProbeFollowsCamera;
    internal Vector3? ProbePoint;
    private Vector3? reportedProbe;
    private IReadOnlyList<TriggerVolume> probeInside = [];
    private long probeReportTime;
    internal event Action<Vector3, IReadOnlyList<TriggerVolume>>? ProbeChanged;
    internal void ResetProbe()
    {
        ProbeFollowsCamera = false;
        ProbePoint = reportedProbe = null;
        probeInside = [];
        probeReportTime = 0;
        Invalidate();
    }
    internal Vector3? UpdateProbe()
    {
        Vector3? probe = ProbeFollowsCamera ? CameraEye() : ProbePoint;
        if (probe is not Vector3 point)
            return null;
        var inside = areaBoxes.Where(z => ZoneVisible(z) && z.Contains(point)).ToArray();
        if (reportedProbe == null || (reportedProbe != point && Environment.TickCount64 - probeReportTime >= 100) || !inside.SequenceEqual(probeInside))
        {
            reportedProbe = point;
            probeReportTime = Environment.TickCount64;
            probeInside = inside;
            ProbeChanged?.Invoke(point, inside);
        }
        return probe;
    }
    internal bool ZoneMatches(TriggerVolume zone) =>
        (ShowFogZones || !zone.IsFog) &&
        (ShowFallZones || !zone.FallKind.HasFlag(FallZoneKind.FallContact)) &&
        (ShowItemFallZones || !zone.FallKind.HasFlag(FallZoneKind.ItemFall)) &&
        (!OnlyFallZones || zone.FallKind.HasFlag(FallZoneKind.FallContact)) &&
        (ShowApproximate || zone.Evidence != ZoneEvidence.Approximate) &&
        (ZoneType.Length == 0 || string.Equals(zone.Actor.Name, ZoneType, StringComparison.Ordinal)) &&
        ActorNameFilter.Matches(zone.Actor, ZoneSearch) &&
        (!SelectedZonesOnly || ReferenceEquals(Selected, zone.Actor));
    internal bool ZoneVisible(TriggerVolume zone) => ShowAreaExtents && ZoneMatches(zone);
    internal bool ZonePickable(TriggerVolume zone) => (PickTriggers || ShowAreaExtents) && ZoneMatches(zone);
    internal bool ZoneHighlighted(TriggerVolume zone) => ReferenceEquals(zone.Actor, Selected) && ZonePickable(zone);
    internal int AreaExtentCount => areaBoxes.Count;
    internal bool ShowLinks = true;
    internal IReadOnlyList<ActorRelationship> Relationships = [];
    internal bool ShowBounds;
    internal int CategoryMask = 127;
    internal int ModeledCount;
    internal Vertex[] World => world;
    internal SceneRange[] WorldRanges => worldRanges;
    internal SceneRange[] BoxRanges => boxRanges;
    internal Vertex[] Boxes => boxes;
    internal bool SuppressRendering = false;
    private Vector3 CameraOffset() => new(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw));
    private Vector3 Forward() => -CameraOffset();
    private Vector3 CameraEye() => center + CameraOffset() * distance;
    internal Matrix4x4 ViewProjection(float aspect)
    {
        var eye = CameraEye();
        return Matrix4x4.CreateLookAt(eye, center, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(0.8f, aspect, 0.1f, Math.Max(10000, distance * 10));
    }
    internal Actor? Selected;
    internal Actor? SelectedModelActor => PickTriggers ? null : Selected;
    private bool HasModel(Actor actor) => models.ContainsKey(actor.Name) && !actor.Name.Contains("FarView", StringComparison.OrdinalIgnoreCase);
    internal bool HasBoundingBox(Actor actor) => actorBounds.ContainsKey(actor);

    public SceneView()
    {
        SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
        TabStop = true;
        animationTimer.Tick += (_, _) =>
        {
            if (!SuppressRendering && Visible && Rendering.Water && Rendering.AnimateWater && worldRanges.Any(r => r.Water is WaterKind.Surface or WaterKind.Waterfall))
                Invalidate();
        };
        animationTimer.Start();
        movementTimer.Tick += (_, _) =>
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            float elapsed = (float)System.Diagnostics.Stopwatch.GetElapsedTime(previousMovementTick, now).TotalSeconds;
            previousMovementTick = now;
            AdvanceMovement(Math.Min(elapsed, 0.1f), (ModifierKeys & Keys.Shift) != 0, (ModifierKeys & Keys.Control) != 0);
        };
    }
    private static Dictionary<string, List<ModelShape>> LoadModels(string? directory)
    {
        var models = new Dictionary<string, List<ModelShape>>();
        if (directory == null || !Directory.Exists(directory))
            return models;
        var indexPath = Path.Combine(directory, "index.json");
        IEnumerable<KeyValuePair<string, string>> files;
        if (File.Exists(indexPath))
        {
            var index = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(indexPath)) ?? throw new InvalidDataException("Invalid geometry index.");
            files = index.Select(pair =>
            {
                if (Path.GetFileName(pair.Value) != pair.Value || !pair.Value.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid geometry filename.");
                return new KeyValuePair<string, string>(pair.Key, Path.Combine(directory, pair.Value));
            }).ToArray();
        }
        else
            files = Directory.EnumerateFiles(directory, "*.json").Select(path => new KeyValuePair<string, string>(Path.GetFileNameWithoutExtension(path), path));
        foreach (var (name, path) in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var shapes = new List<ModelShape>();
                foreach (var shape in doc.RootElement.GetProperty("shapes").EnumerateArray())
                {
                    var verts = shape.GetProperty("vertices").EnumerateArray().Select(v => new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle())).ToArray();
                    var indices = shape.GetProperty("indices").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                    if (indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= verts.Length))
                        throw new InvalidDataException("Invalid geometry indices.");
                    Vector2[]? uvs = null;
                    string? texture = null, emission = null;
                    if (shape.TryGetProperty("uv", out var uvValue))
                    {
                        uvs = uvValue.EnumerateArray().Select(v => new Vector2(v[0].GetSingle(), v[1].GetSingle())).ToArray();
                        if (uvs.Length != verts.Length)
                            uvs = null;
                        string? TexturePath(string key)
                        {
                            if (uvs == null || !shape.TryGetProperty(key, out var value))
                                return null;
                            var filename = value.GetString();
                            if (filename == null || Path.GetFileName(filename) != filename || !filename.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
                                return null;
                            var candidate = Path.Combine(directory, filename);
                            return File.Exists(candidate) ? candidate : null;
                        }
                        texture = TexturePath("texture");
                        emission = TexturePath("emissionTexture");
                    }
                    Vector3[]? normals = null;
                    if (shape.TryGetProperty("normals", out var normalValue))
                    {
                        normals = normalValue.EnumerateArray().Select(v => new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle())).ToArray();
                        if (normals.Length != verts.Length)
                            normals = null;
                    }
                    shapes.Add(new ModelShape(shape.GetProperty("shape").GetString() ?? "", verts, indices, uvs, texture, normals, emission));
                }
                if (shapes.Count > 0)
                    models[name] = shapes;
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or KeyNotFoundException or InvalidDataException) { continue; }
        }
        return models;
    }
    public void SetActors(List<Actor> data, string? geometryDirectory, ActorGraph? graph = null)
    {
        if (!string.Equals(modelDirectory, geometryDirectory, StringComparison.OrdinalIgnoreCase))
        {
            models = LoadModels(geometryDirectory);
            modelDirectory = geometryDirectory;
        }
        actors = data;
        Selected = null;
        Relationships = [];
        areaBoxes.Clear();
        foreach (var actor in data)
            areaBoxes.AddRange(TriggerVolume.Read(actor, graph?.Definitions(actor), graph?.FallKind(actor) ?? FallZoneKind.None));
        ProbePoint = reportedProbe = null;
        probeInside = [];
        if (data.Count > 0)
        {
            var min = data.Select(a => a.Position).Aggregate(Vector3.Min);
            var max = data.Select(a => a.Position).Aggregate(Vector3.Max);
            center = (min + max) / 2;
            distance = Math.Max(50, Vector3.Distance(min, max) * 0.72f);
        }
        if (TerrainScene.FocusBounds(geometryDirectory) is { } focus)
        {
            center = (focus.Min + focus.Max) / 2;
            distance = Math.Max(50, Vector3.Distance(focus.Min, focus.Max) * 0.8f);
        }
        ModeledCount = data.Count(HasModel);
        var count = data.Where(HasModel).Sum(a => models[a.Name].Sum(s => (long)s.Indices.Length));
        if (count > int.MaxValue)
            throw new InvalidOperationException("Scene is too large to build a geometry buffer.");
        var terrain = TerrainScene.Load(geometryDirectory);
        TerrainTileCount = terrain.Tiles;
        world = new Vertex[checked((int)count + terrain.Vertices.Length)];
        int cursor = 0;
        var boxList = new List<Vertex>(ModeledCount * 24);
        var ranges = new List<SceneRange>();
        var boundRanges = new List<SceneRange>();
        actorBounds.Clear();
        foreach (var category in SceneCategories.All)
        {
            foreach (var actor in data)
            {
                if (SceneCategories.Get(actor.Name).Mask != category.Mask || !HasModel(actor) || !models.TryGetValue(actor.Name, out var shapes))
                    continue;
                var transform = Matrix4x4.CreateScale(actor.Scale) * Matrix4x4.CreateRotationX(actor.Rotation.X) * Matrix4x4.CreateRotationY(actor.Rotation.Y) * Matrix4x4.CreateRotationZ(actor.Rotation.Z) * Matrix4x4.CreateTranslation(actor.Position);
                Matrix4x4.Invert(transform, out var inverse);
                var normalTransform = Matrix4x4.Transpose(inverse);
                var min = new Vector3(float.PositiveInfinity);
                var max = new Vector3(float.NegativeInfinity);
                foreach (var shape in shapes)
                {
                    int shapeStart = cursor;
                    var transformed = shape.Vertices.Select(v => Vector3.Transform(v, transform)).ToArray();
                    foreach (var point in transformed)
                    {
                        min = Vector3.Min(min, point);
                        max = Vector3.Max(max, point);
                    }
                    var key = actor.Name + shape.Name;
                    bool green = key.Contains("Plant", StringComparison.OrdinalIgnoreCase) || key.Contains("Grass", StringComparison.OrdinalIgnoreCase) || key.Contains("Leaf", StringComparison.OrdinalIgnoreCase) || key.Contains("Branch", StringComparison.OrdinalIgnoreCase);
                    bool rock = key.Contains("Rock", StringComparison.OrdinalIgnoreCase) || key.Contains("Cliff", StringComparison.OrdinalIgnoreCase);
                    for (int i = 0; i + 2 < shape.Indices.Length; i += 3)
                    {
                        var a = transformed[shape.Indices[i]];
                        var b = transformed[shape.Indices[i + 1]];
                        var c = transformed[shape.Indices[i + 2]];
                        var normal = Vector3.Cross(b - a, c - a);
                        float length = normal.Length();
                        float r = green ? 0.25f : rock ? 0.48f : 0.52f, g = green ? 0.56f : rock ? 0.48f : 0.43f, bcol = green ? 0.24f : rock ? 0.43f : 0.30f;
                        Vertex V(Vector3 p, int index)
                        {
                            var uv = shape.Uvs?[index] ?? Vector2.Zero;
                            var n = shape.Normals == null ? Vector3.Zero : Vector3.TransformNormal(shape.Normals[index], normalTransform);
                            if (n.LengthSquared() > 0.0001f && float.IsFinite(n.LengthSquared()))
                                n = Vector3.Normalize(n);
                            else
                                n = length > 1e-8f ? normal / length : Vector3.UnitY;
                            return new(p.X, p.Y, p.Z, r, g, bcol, 1, uv.X, uv.Y, n.X, n.Y, n.Z);
                        }
                        world[cursor++] = V(a, shape.Indices[i]);
                        world[cursor++] = V(b, shape.Indices[i + 1]);
                        world[cursor++] = V(c, shape.Indices[i + 2]);
                    }
                    if (cursor > shapeStart)
                        ranges.Add(new SceneRange((uint)shapeStart, (uint)(cursor - shapeStart), category.Mask, shape.Texture, Owner: actor,
                            Water: WaterMaterial.Classify(actor.Name, shape.Name), Center: (transformed.Aggregate(Vector3.Min) + transformed.Aggregate(Vector3.Max)) / 2, EmissionTexture: shape.EmissionTexture));
                }
                if (float.IsFinite(min.X) && float.IsFinite(max.X))
                {
                    actorBounds[actor] = (min, max);
                    int boxStart = boxList.Count;
                    boxList.AddRange(BoxLines(min, max, 0.25f, 0.75f, 1));
                    boundRanges.Add(new SceneRange((uint)boxStart, 24, category.Mask, Owner: actor));
                }
            }
        }
        Array.Copy(terrain.Vertices, 0, world, cursor, terrain.Vertices.Length);
        ranges.AddRange(terrain.Ranges.Select(r => r with { Start = r.Start + (uint)cursor }));
        cursor += terrain.Vertices.Length;
        if (cursor != world.Length)
            Array.Resize(ref world, cursor);
        boxes = boxList.ToArray();
        worldRanges = ranges.ToArray();
        boxRanges = boundRanges.ToArray();
        picker = new ScenePicking(world, worldRanges);
        SetNameFilter(nameFilter, FilterModels);
        renderer?.SetWorld(world, boxes, worldRanges, boxRanges);
        Invalidate();
    }
    private static Vertex[] BoxLines(Vector3 min, Vector3 max, float r, float g, float b)
    {
        Vector3[] p = [new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z)];
        int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        return edges.Select(i => new Vertex(p[i].X, p[i].Y, p[i].Z, r, g, b, 1)).ToArray();
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (SuppressRendering)
            return;
        try
        {
            renderer = new Direct3DRenderer(Handle);
            if (world.Length > 0)
                renderer.SetWorld(world, boxes, worldRanges, boxRanges);
        }
        catch (Exception ex) { MessageBox.Show($"Direct3D 11 initialization failed: {ex.Message}"); }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        renderer?.Dispose();
        renderer = null;
        base.OnHandleDestroyed(e);
    }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        previous = mouseDown = e.Location;
        mouseDragged = false;
        Focus();
        base.OnMouseDown(e);
    }
    internal Actor? PickAt(Point point)
    {
        if (Width <= 0 || Height <= 0 || !ClientRectangle.Contains(point) || !ScenePicking.Ray(point, ClientSize, ViewProjection((float)Width / Height), out var origin, out var direction))
            return null;
        if (PickTriggers)
            return TriggerVolume.PickSmallest(areaBoxes.Where(ZonePickable), origin, direction)?.Actor;
        if (PickExtents)
        {
            Actor? selected = null;
            float best = float.PositiveInfinity;
            void Consider(Actor actor, Vector3 o, Vector3 d, Vector3 min, Vector3 max)
            {
                if (!ActorVisible(actor) || !ScenePicking.Box(o, d, min, max, out float near, out float far))
                    return;
                float hit = near > 0.001f ? near : far;
                if (hit < best)
                {
                    best = hit;
                    selected = actor;
                }
            }
            if (ShowBounds)
                foreach (var pair in actorBounds)
                    Consider(pair.Key, origin, direction, pair.Value.Min, pair.Value.Max);
            if (ShowAreaExtents)
                foreach (var box in areaBoxes)
                {
                    if (ZonePickable(box) && box.Ray(origin, direction, out float hit) && hit < best)
                    {
                        best = hit;
                        selected = box.Actor;
                    }
                }
            if (selected != null)
                return selected;
        }
        else
        {
            var hit = picker?.Pick(origin, direction, RangeVisible, MatchesName);
            if (hit != null)
                return hit;
        }
        // Visible marker overlays remain selectable for records with no render mesh.
        var matrix = ViewProjection((float)Width / Height);
        Actor? closest = null;
        float bestPixel = 100;
        foreach (var actor in actors)
        {
            if (!ActorVisible(actor) || (!ShowMarkers && !(ShowCandidates && SceneCategories.IsCandidate(actor.Name))))
                continue;
            var p = Vector4.Transform(new Vector4(actor.Position, 1), matrix);
            if (p.W <= 0 || p.Z / p.W < 0 || p.Z / p.W > 1)
                continue;
            float dx = (p.X / p.W * 0.5f + 0.5f) * Width - point.X, dy = (0.5f - p.Y / p.W * 0.5f) * Height - point.Y, d = dx * dx + dy * dy;
            if (d < bestPixel)
            {
                bestPixel = d;
                closest = actor;
            }
        }
        return closest;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !mouseDragged && Math.Abs(e.X - mouseDown.X) < 4 && Math.Abs(e.Y - mouseDown.Y) < 4)
            ActorPicked?.Invoke(PickAt(e.Location));
        base.OnMouseUp(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.None && (Math.Abs(e.X - mouseDown.X) >= 4 || Math.Abs(e.Y - mouseDown.Y) >= 4))
            mouseDragged = true;
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
        {
            LookRelative(e.X - previous.X, e.Y - previous.Y);
        }
        if (e.Button == MouseButtons.Middle)
        {
            var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
            var up = Vector3.Cross(right, Forward());
            center += (-right * (e.X - previous.X) + up * (e.Y - previous.Y)) * distance / Math.Max(1, Height);
            Invalidate();
        }
        previous = e.Location;
        base.OnMouseMove(e);
    }
    private void LookRelative(int dx, int dy)
    {
        var eye = CameraEye();
        yaw -= dx * 0.005f;
        pitch = Math.Clamp(pitch + dy * 0.005f, -1.55f, 1.55f);
        center = eye - CameraOffset() * distance;
        Invalidate();
    }
    internal void FocusActor(Actor actor)
    {
        Selected = actor;
        if (actorBounds.TryGetValue(actor, out var box))
        {
            center = (box.Min + box.Max) / 2;
            distance = Math.Max(6, Vector3.Distance(box.Min, box.Max) * 1.1f);
        }
        else if (areaBoxes.FirstOrDefault(z => ReferenceEquals(z.Actor, actor)) is { } zone)
        {
            center = zone.Center;
            distance = Math.Max(6, zone.Radius * 2.5f);
        }
        else
        {
            center = actor.Position;
            distance = 40;
        }
        Invalidate();
    }
    internal void FrameScene()
    {
        if (actors.Count == 0)
            return;
        if (TerrainScene.FocusBounds(modelDirectory) is { } focus)
        {
            center = (focus.Min + focus.Max) / 2;
            distance = Math.Max(50, Vector3.Distance(focus.Min, focus.Max) * 0.8f);
        }
        else
        {
            var min = actors.Select(a => a.Position).Aggregate(Vector3.Min);
            var max = actors.Select(a => a.Position).Aggregate(Vector3.Max);
            center = (min + max) / 2;
            distance = Math.Max(50, Vector3.Distance(min, max) * 0.72f);
        }
        Invalidate();
    }
    internal void TopView()
    {
        pitch = 1.54f;
        Invalidate();
    }
    private static int MovementBit(Keys key) => key switch { Keys.W => 1, Keys.S => 2, Keys.A => 4, Keys.D => 8, Keys.F or Keys.Q => 16, Keys.R or Keys.E => 32, _ => 0 };
    private void AdvanceMovement(float elapsed, bool fast, bool slow = false)
    {
        var forward = Forward();
        var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        var direction = forward * ((movementKeys & 1) != 0 ? 1 : 0) - forward * ((movementKeys & 2) != 0 ? 1 : 0)
            + right * ((movementKeys & 8) != 0 ? 1 : 0) - right * ((movementKeys & 4) != 0 ? 1 : 0)
            + Vector3.Cross(right, forward) * (((movementKeys & 32) != 0 ? 1 : 0) - ((movementKeys & 16) != 0 ? 1 : 0));
        if (direction.LengthSquared() == 0)
            return;
        center += direction * flySpeed * (fast ? 5 : 1) * (slow ? 0.2f : 1) * elapsed;
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int bit = MovementBit(e.KeyCode);
        if (bit != 0)
        {
            movementKeys |= bit;
            if (!movementTimer.Enabled)
            {
                previousMovementTick = System.Diagnostics.Stopwatch.GetTimestamp();
                movementTimer.Start();
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        if (e.KeyCode == Keys.T)
        {
            TopView();
            e.Handled = true;
        }
        if (e.KeyCode == Keys.Home && Selected != null)
        {
            FocusActor(Selected);
            e.Handled = true;
        }
        if (e.KeyCode == Keys.Z || e.KeyCode == Keys.X)
        {
            AdjustFlySpeed(e.KeyCode == Keys.Z ? 1 / 1.1f : 1.1f);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        int bit = MovementBit(e.KeyCode);
        if (bit != 0)
        {
            movementKeys &= ~bit;
            if (movementKeys == 0)
                movementTimer.Stop();
            e.Handled = true;
        }
        base.OnKeyUp(e);
    }
    protected override void OnLostFocus(EventArgs e)
    {
        movementKeys = 0;
        movementTimer.Stop();
        base.OnLostFocus(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            movementTimer.Dispose();
            animationTimer.Dispose();
        }
        base.Dispose(disposing);
    }
    private void AdjustFlySpeed(float factor)
    {
        flySpeed = Math.Clamp(flySpeed * factor, 0.1f, 5000);
        FlySpeedChanged?.Invoke(flySpeed);
        Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        AdjustFlySpeed(MathF.Pow(1.1f, -e.Delta / 120f));
        base.OnMouseWheel(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SuppressRendering)
        {
            e.Graphics.Clear(Color.FromArgb(9, 14, 20));
            return;
        }
        if (renderer == null || Width == 0 || Height == 0)
            return;
        var matrix = ViewProjection((float)Width / Height);
        var points = new List<(float depth, Vector3 p, bool selected, bool candidate)>();
        foreach (var actor in actors)
        {
            bool candidate = SceneCategories.IsCandidate(actor.Name);
            if (!ShowMarkers && !(ShowCandidates && candidate) && !ReferenceEquals(actor, Selected))
                continue;
            if (!ActorVisible(actor))
                continue;
            var p = Vector4.Transform(new Vector4(actor.Position, 1), matrix);
            if (p.W <= 0)
                continue;
            var v = new Vector3(p.X / p.W, p.Y / p.W, p.Z / p.W);
            if (v.Z < 0 || v.Z > 1 || Math.Abs(v.X) > 1.1f || Math.Abs(v.Y) > 1.1f)
                continue;
            points.Add((p.W, v, ReferenceEquals(actor, Selected), candidate && ShowCandidates));
        }
        var vertices = new List<Vertex>(points.Count * 6);
        foreach (var (_, p, selected, candidate) in points.OrderByDescending(x => x.depth))
        {
            float sx = (selected ? 7f : candidate ? 4f : 2.5f) * 2 / Width, sy = (selected ? 7f : candidate ? 4f : 2.5f) * 2 / Height;
            float r = selected ? 1f : candidate ? 0.95f : 0.32f, g = selected ? 0.75f : candidate ? 0.35f : 0.8f, b = selected ? 0.18f : candidate ? 0.75f : 0.9f;
            Vertex V(float x, float y) => new(x, y, p.Z, r, g, b, 1);
            vertices.AddRange([V(p.X - sx, p.Y - sy), V(p.X + sx, p.Y - sy), V(p.X + sx, p.Y + sy), V(p.X - sx, p.Y - sy), V(p.X + sx, p.Y + sy), V(p.X - sx, p.Y + sy)]);
        }
        var selectionLines = new List<Vertex>();
        var zoneLines = new List<Vertex>();
        var zoneFaces = new List<Vertex>();
        Vector3? probe = UpdateProbe();
        if (probe is Vector3 point && !ProbeFollowsCamera)
            selectionLines.AddRange(BoxLines(point - new Vector3(0.3f), point + new Vector3(0.3f), 0.4f, 1, 0.4f));
        if (ShowAreaExtents)
            foreach (var zone in areaBoxes.Where(ZoneVisible).OrderByDescending(z => Vector3.DistanceSquared(CameraEye(), z.Center)))
            {
                bool active = probe is Vector3 pos && zone.Contains(pos);
                foreach (var source in zone.Lines)
                {
                    var v = source;
                    if (active)
                    {
                        v.R = 0.3f;
                        v.G = 1;
                        v.B = 0.4f;
                    }
                    zoneLines.Add(v);
                }
                if (FillZones)
                    zoneFaces.AddRange(zone.Faces);
            }
        if (Selected != null && (ActorVisible(Selected) || areaBoxes.Any(ZoneHighlighted)))
        {
            foreach (var area in areaBoxes.Where(ZoneHighlighted))
                foreach (var line in area.Lines)
                {
                    var v = line;
                    v.R = 1;
                    v.G = 0.72f;
                    v.B = 0.1f;
                    selectionLines.Add(v);
                }
            if (SelectedModelActor != null && actorBounds.TryGetValue(SelectedModelActor, out var selectedExtent))
                selectionLines.AddRange(BoxLines(selectedExtent.Min, selectedExtent.Max, 1, 0.72f, 0.2f));
            if (ShowLinks)
                foreach (var link in Relationships)
                    if (link.Target is Actor other && ActorVisible(other))
                    {
                        var a = Selected.Position;
                        var b = other.Position;
                        float r = link.Direction == "Outgoing" ? 1 : 0.25f, g = link.Direction == "Outgoing" ? 0.65f : 0.8f;
                        selectionLines.Add(new Vertex(a.X, a.Y, a.Z, r, g, 1, 1));
                        selectionLines.Add(new Vertex(b.X, b.Y, b.Z, r, g, 1, 1));
                    }
        }
        renderer.Draw(Width, Height, matrix, vertices.ToArray(), CategoryMask, ShowBounds, selectionLines.ToArray(), GameRendering, ShowTerrain, nameMatches, FilterModels, SelectedModelActor, Rendering, CameraEye(), zoneFaces.ToArray(), zoneLines.ToArray(), ZoneXray);
    }
}

record ModelShape(string Name, Vector3[] Vertices, int[] Indices, Vector2[]? Uvs, string? Texture, Vector3[]? Normals = null, string? EmissionTexture = null);
