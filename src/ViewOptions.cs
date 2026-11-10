using System.Numerics;
using SpaceUI;

namespace OotSceneViewer;

public partial class Form1
{
    private readonly SpaceToggle treeZonesOnly = Toggle("Tree: zones and events only", false);
    private readonly SpaceToggle pickZones = Toggle("Click to select zone volumes", false);
    private readonly SpaceToggle fillZones = Toggle("Translucent zone fill", true);
    private readonly SpaceToggle zoneXray = Toggle("Show zones through walls", true);
    private readonly SpaceToggle approximateZones = Toggle("Include approximate volumes", true);
    private readonly SpaceToggle fallZones = Toggle("Death / fall candidates", true);
    private readonly SpaceToggle itemFallZones = Toggle("Item fall zones", true);
    private readonly SpaceToggle selectedZoneOnly = Toggle("Isolate selected zone", false);
    private readonly SpaceToggle followProbe = Toggle("Probe follows fly camera", false);
    private readonly SpaceSearchBox zoneSearch = new() { Width = 320, Height = 32, PlaceholderText = "Filter map zones by name…" };
    private readonly SpaceComboBox zoneType = new() { Width = 320, Height = 32, MaxDropDownItems = 10 };
    private readonly SpaceListBox zoneList = new() { Width = 320, Height = 145, HorizontalScrollbar = true };
    private readonly SpaceListBox insideList = new() { Width = 320, Height = 85, HorizontalScrollbar = true };
    private readonly Label zoneCount = Note("No scene loaded", 42);
    private readonly Label probeInfo = Note("Entry preview is off. Place a probe or follow the camera.", 55);
    private readonly Label probeTransition = Note("This tests a point, not a simulated player capsule or game logic.", 55);
    private IReadOnlyList<TriggerVolume> lastInside = [];
    private bool refreshingZones;
    private readonly SpaceToggle wire = Toggle("World wireframe", false);

    private void ResetProbePreview()
    {
        view.ResetProbe();
        lastInside = [];
        insideList.Items.Clear();
        probeInfo.Text = "Entry preview off. Place a probe or follow the camera.";
        probeTransition.Text = "This tests a point, not a simulated player capsule or game logic.";
    }

    private static Label Note(string text, int height) => new() { Text = text, Width = 320, Height = height, ForeColor = SpaceTheme.Muted, Margin = new Padding(3, 4, 3, 4) };
    private static void Section(FlowLayoutPanel panel, string text) => panel.Controls.Add(new Label { Text = text, Width = 320, Height = 27, ForeColor = SpaceTheme.Accent, TextAlign = ContentAlignment.BottomLeft });
    private Control BuildDisplayOptions()
    {
        var tabs = new SpaceTabControl { Dock = DockStyle.Fill };
        FlowLayoutPanel Page(string name)
        {
            var page = new TabPage(name) { BackColor = SpaceTheme.Surface, Padding = new Padding(4) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = SpaceTheme.Surface };
            page.Controls.Add(new SpaceScrollHost(flow));
            tabs.TabPages.Add(page);
            return flow;
        }
        var scene = Page("Scene");
        var lighting = Page("Lighting");
        var zones = Page("Zones");
        void Bind(FlowLayoutPanel panel, SpaceToggle control, Action<bool> update)
        {
            control.Width = 320;
            panel.Controls.Add(control);
            control.CheckedChanged += (_, _) => { update(control.Checked); view.Invalidate(); };
        }
        var presets = new FlowLayoutPanel { Width = 320, Height = 36, WrapContents = false };
        scene.Controls.Add(presets);
        presets.Controls.Add(Button("Clean", 94, (_, _) =>
        {
            markers.Checked = candidates.Checked = areaExtents.Checked = bounds.Checked = pickExtents.Checked = pickZones.Checked = followProbe.Checked = treeZonesOnly.Checked = false;
            gameRendering.Checked = terrain.Checked = true;
            wire.Checked = false;
            ResetProbePreview();
        }));
        presets.Controls.Add(Button("Inspect", 94, (_, _) =>
        {
            markers.Checked = selectedLinks.Checked = true;
            pickZones.Checked = pickExtents.Checked = treeZonesOnly.Checked = followProbe.Checked = false;
            gameRendering.Checked = false;
            wire.Checked = areaExtents.Checked = candidates.Checked = false;
            ResetProbePreview();
        }));
        presets.Controls.Add(Button("Triggers", 94, (_, _) =>
        {
            areaExtents.Checked = pickZones.Checked = selectedLinks.Checked = treeZonesOnly.Checked = true;
            markers.Checked = candidates.Checked = bounds.Checked = pickExtents.Checked = false;
            tabs.SelectedIndex = 2;
            details.ShowTriggerTab();
        }));
        Section(scene, "WORLD");
        Bind(scene, gameRendering, v => view.GameRendering = v);
        Bind(scene, terrain, v => view.ShowTerrain = v);
        Bind(scene, wire, v => view.Rendering.Wireframe = v);
        Bind(scene, Toggle("Water shader", true), v => view.Rendering.Water = v);
        Bind(scene, Toggle("Animate water", true), v => view.Rendering.AnimateWater = v);
        Section(scene, "SELECTION & OVERLAYS");
        scene.Controls.Add(extentFilter);
        scene.Controls.Add(extentMatches);
        void ApplyNameFilter()
        {
            view.SetNameFilter(extentFilter.Text, filterModels.Checked);
            extentMatches.Text = $"{view.NameMatchCount:N0} / {actors.Count:N0} names match · comma = OR";
            RefreshZones();
        }
        extentFilter.TextChanged += (_, _) => ApplyNameFilter();
        Bind(scene, filterModels, _ => ApplyNameFilter());
        Bind(scene, pickExtents, v => { view.PickExtents = v; if (v) { bounds.Checked = true; pickZones.Checked = false; } });
        Bind(scene, bounds, v => view.ShowBounds = v);
        Bind(scene, markers, v => view.ShowMarkers = v);
        Bind(scene, candidates, v => view.ShowCandidates = v);
        Bind(scene, selectedLinks, v => view.ShowLinks = v);
        Bind(scene, treeZonesOnly, _ => RefreshTree());
        Section(scene, "NAME-BASED CATEGORIES");
        foreach (var category in SceneCategories.All)
        {
            var checkbox = new SpaceCheckBox { Text = category.Name, UseMnemonic = false, Checked = true, AutoSize = false, Width = 320, Height = 28, Tag = category.Mask };
            checkbox.CheckedChanged += (_, _) => { var bit = (int)checkbox.Tag; view.CategoryMask = checkbox.Checked ? view.CategoryMask | bit : view.CategoryMask & ~bit; RefreshZones(); view.Invalidate(); };
            categoryControls[category.Mask] = checkbox;
            scene.Controls.Add(checkbox);
        }

        Section(lighting, "VIEWER LIGHTING");
        lighting.Controls.Add(Note("Reconstructed shading using original albedo textures. Shadows and original game material effects are not simulated.", 55));
        var light = Toggle("Directional + sky lighting", true);
        var fog = Toggle("Distance haze", true);
        Bind(lighting, light, v => view.Rendering.Lighting = v);
        Bind(lighting, fog, v => view.Rendering.Fog = v);
        void Value(string title, decimal min, decimal max, decimal initial, Action<float> update, int decimals = 2)
        {
            lighting.Controls.Add(Note(title, 21));
            var editor = new SpaceValueEditor { Width = 320, Height = 32, Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.05m, ResetValue = initial, Value = initial };
            editor.ValueChanged += (_, _) => { update((float)editor.Value); view.Invalidate(); };
            lighting.Controls.Add(editor);
        }
        Value("Sun direction (degrees)", 0, 360, 135, v => view.Rendering.SunAzimuth = v, 0);
        Value("Sun elevation (degrees)", 0, 90, 45, v => view.Rendering.SunElevation = v, 0);
        Value("Sun intensity", 0, 4, 1.4m, v => view.Rendering.SunStrength = v);
        Value("Sky / ground ambient", 0, 2, 0.55m, v => view.Rendering.Ambient = v);
        Value("Exposure", 0.25m, 3, 1.15m, v => view.Rendering.Exposure = v);
        Value("Haze distance (world units)", 100, 10000, 2500, v => view.Rendering.FogDistance = v, 0);
        Section(lighting, "WATER");
        lighting.Controls.Add(Note("Viewer water effect on named water materials: animated ripples, sky reflection and waterfall streaks. No scene reflections or refraction.", 60));
        Value("Water ripples", 0, 0.6m, 0.18m, v => view.Rendering.WaterRipples = v);
        Value("Water opacity", 0.1m, 1, 0.72m, v => view.Rendering.WaterOpacity = v);
        Value("Water flow speed", 0, 3, 1, v => view.Rendering.WaterSpeed = v);

        Section(zones, "VISIBLE ZONES ON MAP");
        areaExtents.Text = "Show zones on map";
        Bind(zones, areaExtents, v => { view.ShowAreaExtents = v; RefreshZones(); });
        zones.Controls.Add(zoneSearch);
        zoneType.Items.Add("All zone types");
        zoneType.SelectedIndex = 0;
        zones.Controls.Add(zoneType);
        zones.Controls.Add(Note("Filters update the map and list together. Commas match either name. World models stay visible.", 42));
        var visibility = new FlowLayoutPanel { Width = 320, Height = 36, WrapContents = false };
        visibility.Controls.Add(Button("Show all", 145, (_, _) => ShowAllZones()));
        visibility.Controls.Add(Button("Hide all", 145, (_, _) => areaExtents.Checked = false));
        zones.Controls.Add(visibility);
        Bind(zones, selectedZoneOnly, v => { view.SelectedZonesOnly = v; if (v) areaExtents.Checked = true; RefreshZones(); });
        Bind(zones, approximateZones, v => { view.ShowApproximate = v; RefreshZones(); });
        Bind(zones, fallZones, v => { view.ShowFallZones = v; RefreshZones(); });
        Bind(zones, itemFallZones, v => { view.ShowItemFallZones = v; RefreshZones(); });
        zones.Controls.Add(Button("Only death / fall candidates", 300, (_, _) =>
        {
            ShowAllZones();
            view.OnlyFallZones = true;
            pickZones.Checked = true;
            RefreshZones();
            view.Invalidate();
        }));
        zones.Controls.Add(Note("Identified from FallDown logic references. Lethal/reset behavior is unverified. Item-fall areas are separate.", 48));
        zones.Controls.Add(zoneCount);
        zones.Controls.Add(new SpaceScrollHost(zoneList) { Size = new Size(320, 145) });
        zoneSearch.TextChanged += (_, _) => { view.ZoneSearch = zoneSearch.Text; areaExtents.Checked = true; RefreshZones(); view.Invalidate(); };
        zoneType.SelectedIndexChanged += (_, _) =>
        {
            if (refreshingZones)
                return;
            view.ZoneType = zoneType.SelectedIndex > 0 ? zoneType.SelectedItem?.ToString() ?? "" : "";
            areaExtents.Checked = true;
            RefreshZones();
            view.Invalidate();
        };
        Section(zones, "APPEARANCE & PICKING");
        Bind(zones, pickZones, v => { view.PickTriggers = v; if (v) { areaExtents.Checked = true; pickExtents.Checked = false; details.ShowTriggerTab(); } });
        Bind(zones, fillZones, v => view.FillZones = v);
        Bind(zones, zoneXray, v => view.ZoneXray = v);
        zones.Controls.Add(Note("Cyan: resource shapes/bounds. Amber: approximate. Green: probe inside. Inspection volumes are not verified player collision.", 55));
        zoneList.SelectedIndexChanged += (_, _) => { if (!refreshingZones && zoneList.SelectedItem is TriggerVolume volume) { SelectFromViewport(volume.Actor); details.ShowTriggerTab(); } };
        zoneList.DoubleClick += (_, _) => { if (zoneList.SelectedItem is TriggerVolume volume) view.FocusActor(volume.Actor); };
        var actions = new FlowLayoutPanel { Width = 320, Height = 36, WrapContents = false };
        actions.Controls.Add(Button("Focus zone", 125, (_, _) => { if (SelectedZone() is { } zone) view.FocusActor(zone.Actor); }));
        actions.Controls.Add(Button("Probe at center", 145, (_, _) =>
        {
            if (SelectedZone() is not { } zone)
                return;
            followProbe.Checked = false;
            ResetProbePreview();
            areaExtents.Checked = true;
            view.ProbePoint = zone.Center;
            view.Invalidate();
        }));
        zones.Controls.Add(actions);
        zones.Controls.Add(Button("Open selection details", 300, (_, _) => OpenTriggerDetails()));
        Section(zones, "ENTRY / EXIT PREVIEW");
        var inspectEntry = Toggle("Inspect zone when probe enters", false);
        Bind(zones, followProbe, v =>
        {
            ResetProbePreview();
            view.ProbeFollowsCamera = v;
            if (v)
                areaExtents.Checked = true;
        });
        zones.Controls.Add(inspectEntry);
        zones.Controls.Add(probeInfo);
        zones.Controls.Add(new SpaceScrollHost(insideList) { Size = new Size(320, 85) });
        zones.Controls.Add(probeTransition);
        insideList.DoubleClick += (_, _) => { if (insideList.SelectedItem is TriggerVolume zone) { SelectFromViewport(zone.Actor); OpenTriggerDetails(); } };
        view.ProbeChanged += (point, inside) =>
        {
            probeInfo.Text = $"Probe {point.X:F1}, {point.Y:F1}, {point.Z:F1}\nInside {inside.Count} visible inspection volumes.\nDouble-click an entry to inspect configured behavior.";
            if (inside.SequenceEqual(lastInside))
                return;
            var entered = inside.Except(lastInside).Select(z => z.Actor.Name).Distinct().Take(2).ToArray();
            var enteredZone = inside.Except(lastInside).FirstOrDefault();
            var exited = lastInside.Except(inside).Select(z => z.Actor.Name).Distinct().Take(2).ToArray();
            probeTransition.Text = (entered.Length > 0 ? "Entered: " + string.Join(", ", entered) + "\n" : "") + (exited.Length > 0 ? "Left: " + string.Join(", ", exited) : "") + "\nGeometry preview only; game events are not executed.";
            lastInside = inside.ToArray();
            insideList.BeginUpdate();
            insideList.Items.Clear();
            insideList.Items.AddRange(inside.Cast<object>().ToArray());
            insideList.EndUpdate();
            if (inspectEntry.Checked && enteredZone != null)
            {
                SelectFromViewport(enteredZone.Actor);
                OpenTriggerDetails();
            }
        };
        return tabs;
    }

    private TriggerVolume? SelectedZone() => zoneList.SelectedItem as TriggerVolume ?? view.Zones.FirstOrDefault(z => ReferenceEquals(z.Actor, view.Selected));
    private void OpenTriggerDetails()
    {
        details.ShowTriggerTab();
        sidebarTabs.SelectedIndex = 0;
    }
    private void ShowAllZones()
    {
        view.OnlyFallZones = false;
        fallZones.Checked = itemFallZones.Checked = true;
        selectedZoneOnly.Checked = false;
        approximateZones.Checked = true;
        zoneSearch.Text = "";
        zoneType.SelectedIndex = 0;
        areaExtents.Checked = true;
        RefreshZones();
        view.Invalidate();
    }
    private void PopulateZoneTypes()
    {
        refreshingZones = true;
        try
        {
            zoneType.Items.Clear();
            zoneType.Items.Add("All zone types");
            zoneType.Items.AddRange(view.Zones.Select(z => z.Actor.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
            zoneType.SelectedIndex = 0;
            view.ZoneType = "";
        }
        finally { refreshingZones = false; }
    }
    private void RefreshZones()
    {
        if (refreshingZones)
            return;
        refreshingZones = true;
        try
        {
            var previous = view.Zones.FirstOrDefault(z => ReferenceEquals(z.Actor, view.Selected)) ?? zoneList.SelectedItem;
            var zones = view.Zones.Where(view.ZoneMatches).ToArray();
            fallZones.Text = $"Death / fall candidates ({view.Zones.Where(z => z.FallKind.HasFlag(FallZoneKind.FallContact)).Select(z => z.Actor).Distinct().Count()})";
            itemFallZones.Text = $"Item fall zones ({view.Zones.Where(z => z.FallKind.HasFlag(FallZoneKind.ItemFall)).Select(z => z.Actor).Distinct().Count()})";
            zoneList.BeginUpdate();
            zoneList.Items.Clear();
            zoneList.Items.AddRange(zones.Cast<object>().ToArray());
            if (previous != null && zoneList.Items.Contains(previous))
                zoneList.SelectedItem = previous;
            zoneList.EndUpdate();
            zoneCount.Text = !view.ShowAreaExtents ? $"Zones hidden · {zones.Length:N0} match your filters" :
                zones.Length == 0 ? "No zones match. Use Show all to reset filters." :
                $"Showing {zones.Length:N0} of {view.Zones.Count:N0} volumes on map\n" + (view.OnlyFallZones ? "Death / fall only · Show all resets this filter" : $"{zones.Count(z => z.Evidence == ZoneEvidence.Approximate)} approximate · click to select; double-click to focus");
        }
        finally { refreshingZones = false; }
    }
}
