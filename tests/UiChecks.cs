using System.Numerics;
namespace OotSceneViewer;

public partial class Form1
{
    internal void SuppressRenderingForChecks() => view.SuppressRendering = true;
    internal void CheckEmptyStartup()
    {
        if (actors.Count != 0 || tree.Nodes.Count != 0 || view.World.Length != 0)
            throw new InvalidOperationException("A new viewer contains scene data before loading a local file.");
        CreateControl();
        PerformLayout();
        int before = view.Width;
        objectSplit.SplitterDistance += 60;
        if (view.Width >= before)
            throw new InvalidOperationException("Left divider did not resize the map.");
        before = view.Width;
        inspectorSplit.SplitterDistance -= 60;
        if (view.Width >= before)
            throw new InvalidOperationException("Right divider did not resize the map.");
        ClientSize = new Size(1250, 720);
        PerformLayout();
        if (view.Width < 300 || inspectorSplit.Panel2.Width < 380 || objectSplit.Panel1.Width < 220)
            throw new InvalidOperationException("Resizing lost a panel's minimum width.");
    }
    internal string RunUiChecks(string[] paths)
    {
        LoadScene(paths);
        if (actors.Count == 0 || tree.Nodes.Count == 0 || view.ModeledCount == 0)
            throw new InvalidOperationException("Scene hierarchy did not load.");
        var selected = actors.First(a => view.HasBoundingBox(a));
        filter.Text = selected.Name;
        if (actorNodes.Count == 0 || actorNodes.Keys.Any(a => !a.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase) && !a.Raw.GetRawText().Contains(filter.Text, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Tree search returned incorrect objects.");
        tree.SelectedNode = actorNodes[selected];
        SelectActor(selected);
        var bit = SceneCategories.Get(selected.Name).Mask;
        markers.Checked = true;
        candidates.Checked = true;
        bounds.Checked = true;
        categoryControls[bit].Checked = false;
        if (!view.ShowMarkers || !view.ShowCandidates || !view.ShowBounds || (view.CategoryMask & bit) != 0 || view.Selected != selected)
            throw new InvalidOperationException("View controls did not update scene state.");
        categoryControls[bit].Checked = true;
        if ((view.CategoryMask & bit) == 0)
            throw new InvalidOperationException("Category restore failed.");
        if (!SceneCategories.IsCandidate("Area") || !SceneCategories.IsCandidate("EventProxyRequestActor") || SceneCategories.IsCandidate("Obj_LiftRock_A_01") || selected.Raw.GetProperty("Gyaml").GetString() != selected.Name)
            throw new InvalidOperationException("Candidate classification or placement metadata failed.");
        view.CheckMovement();
        if (actors.Any(a => a.Name.Contains("Area", StringComparison.OrdinalIgnoreCase) && a.Raw.TryGetProperty("Scale", out _)) && view.AreaExtentCount == 0)
            throw new InvalidOperationException("Area placement extents were not built.");
        gameRendering.Checked = false;
        areaExtents.Checked = false;
        if (view.GameRendering || view.ShowAreaExtents)
            throw new InvalidOperationException("Rendering toggles did not update the viewport.");
        gameRendering.Checked = true;
        areaExtents.Checked = true;
        terrain.Checked = false;
        if (view.ShowTerrain)
            throw new InvalidOperationException("Terrain toggle did not update the viewport.");
        terrain.Checked = true;
        if (view.TerrainTileCount > 0 && !view.WorldRanges.Any(r => r.Terrain && r.Count > 0))
            throw new InvalidOperationException("Terrain was loaded without drawable geometry.");
        extentFilter.Text = selected.Name;
        filterModels.Checked = true;
        if (view.NameMatchCount == 0 || view.WorldRanges.Any(r => r.Owner != null && view.RangeVisible(r) && !ActorNameFilter.Matches(r.Owner, selected.Name)))
            throw new InvalidOperationException("Viewport model filter failed.");
        extentFilter.Text = "__no_actor_matches__";
        if (view.NameMatchCount != 0 || view.ActorVisible(selected))
            throw new InvalidOperationException("Extent filter failed.");
        extentFilter.Text = "";
        filterModels.Checked = false;
        var flagsActor = actors.FirstOrDefault(a => a.Raw.TryGetProperty("Presence", out _));
        if (flagsActor != null)
        {
            SelectActor(flagsActor);
            if (details.FlagFields.Count == 0)
                throw new InvalidOperationException("Structured flag inspector is empty.");
        }
        view.CheckPicking();
        if (view.Zones.Count > 0)
        {
            filter.Text = "";
            treeZonesOnly.Checked = true;
            if (actorNodes.Keys.Any(a => !TriggerVolume.IsCandidate(a) && !a.Name.Contains("Event", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Zone/event tree filter included unrelated actors.");
            areaExtents.Checked = false;
            // This test form stays hidden; native PerformClick requires visibility.
            typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(mapZoneSelection, [EventArgs.Empty]);
            if (!view.PickTriggers || view.ShowAreaExtents || view.PickExtents || !pickZones.Checked || !selectZonesMenu.Checked)
                throw new InvalidOperationException("Trigger picking controls did not switch selection mode.");
            selectZonesMenu.PerformClick();
            if (view.PickTriggers || mapZoneSelection.Active || !mapObjectSelection.Active || pickZones.Checked || selectZonesMenu.Checked)
                throw new InvalidOperationException("Zone selection menu did not synchronize the map toggle.");
            pickZones.Checked = true;
            if (!mapZoneSelection.Active || mapObjectSelection.Active || !selectZonesMenu.Checked)
                throw new InvalidOperationException("Sidebar picking mode did not synchronize the map/menu.");
            zoneSearch.Text = "Area";
            if (view.ShowAreaExtents)
                throw new InvalidOperationException("Filtering hidden zones turned the overlay on.");
            zoneSearch.Text = "";
            areaExtents.Checked = true;
            approximateZones.Checked = false;
            if (zoneList.Items.Cast<TriggerVolume>().Any(z => z.Evidence == ZoneEvidence.Approximate))
                throw new InvalidOperationException("Approximate-volume filter failed.");
            approximateZones.Checked = true;
            zoneSearch.Text = "__no_zone_matches__";
            if (zoneList.Items.Count != 0 || view.Zones.Any(view.ZoneVisible))
                throw new InvalidOperationException("Zone search did not hide map volumes as well as list entries.");
            zoneSearch.Text = "";
            zoneType.SelectedIndex = 1;
            if (view.Zones.Where(view.ZoneVisible).Any(z => z.Actor.Name != view.ZoneType))
                throw new InvalidOperationException("Zone type filter left other types on the map.");
            var zoneActor = view.Zones.First(view.ZoneVisible).Actor;
            SelectActor(zoneActor);
            selectedZoneOnly.Checked = true;
            if (view.Zones.Where(view.ZoneVisible).Any(z => !ReferenceEquals(z.Actor, zoneActor)))
                throw new InvalidOperationException("Isolate selected did not hide the other zones.");
            areaExtents.Checked = false;
            if (view.Zones.Any(view.ZoneVisible))
                throw new InvalidOperationException("Hide all left zones visible.");
            ShowAllZones();
            if (view.Zones.Count(view.ZoneVisible) != view.Zones.Count || zoneSearch.Text.Length != 0 || zoneType.SelectedIndex != 0)
                throw new InvalidOperationException("Show all did not reset zone filters.");
            int fallCount = view.Zones.Count(z => z.FallKind.HasFlag(FallZoneKind.FallContact));
            fallZones.Checked = false;
            if (view.Zones.Count(view.ZoneVisible) != view.Zones.Count - fallCount || zoneList.Items.Cast<TriggerVolume>().Any(z => z.FallKind.HasFlag(FallZoneKind.FallContact)))
                throw new InvalidOperationException("Fall toggle did not filter both list and map.");
            ShowAllZones();
            Console.WriteLine($"Fall zones: {fallCount} fall-contact candidate volumes, {view.Zones.Count(z => z.FallKind.HasFlag(FallZoneKind.ItemFall))} item-fall volumes.");
            int fogCount = view.Zones.Count(z => z.IsFog);
            fogZones.Checked = false;
            if (view.Zones.Count(view.ZoneMatches) != view.Zones.Count - fogCount || view.Zones.Any(z => z.IsFog && (view.ZonePickable(z) || view.ZoneHighlighted(z))) || zoneList.Items.Cast<TriggerVolume>().Any(z => z.IsFog))
                throw new InvalidOperationException("Fog-zone filter did not exclude drawing, picking and list entries.");
            ShowAllZones();
            if (!fogZones.Checked || view.Zones.Count(view.ZoneMatches) != view.Zones.Count)
                throw new InvalidOperationException("Show all did not restore fog zones.");
            view.CheckZonePicking();
            followProbe.Checked = true;
            if (!view.ProbeFollowsCamera)
                throw new InvalidOperationException("Camera probe toggle failed.");
            followProbe.Checked = false;
            if (view.ProbeFollowsCamera || view.ProbePoint != null)
                throw new InvalidOperationException("Probe reset failed.");
        }
        return $"PASS: {actors.Count} placements, {view.ModeledCount} modeled, {view.AreaExtentCount} inspection volumes ({view.Zones.Count(z => z.Evidence == ZoneEvidence.Primitive)} primitives, {view.Zones.Count(z => z.Evidence == ZoneEvidence.ResourceBounds)} resource bounds), search, selection, zone filters/picking, rendering toggles and fly camera.";
    }
}
sealed partial class SceneView
{
    internal void CheckZonePicking()
    {
        var oldSize = Size;
        bool shown = ShowAreaExtents;
        try
        {
            Size = new Size(800, 600);
            var zone = Zones.First(z => ZoneVisible(z));
            Selected = zone.Actor;
            if (SelectedModelActor != null)
                throw new InvalidOperationException("Zone selection still requested a model highlight.");
            SelectedZonesOnly = true;
            FocusActor(zone.Actor);
            if (!ReferenceEquals(PickAt(new Point(400, 300)), zone.Actor))
                throw new InvalidOperationException("Zone center ray failed to select the focused volume.");
            ShowAreaExtents = false;
            if (!ReferenceEquals(PickAt(new Point(400, 300)), zone.Actor) || ZoneVisible(zone) || !ZoneHighlighted(zone))
                throw new InvalidOperationException("Hidden-zone picking or selected outline failed.");
            ZoneSearch = "__no_zone_matches__";
            if (PickAt(new Point(400, 300)) != null || ZoneHighlighted(zone))
                throw new InvalidOperationException("Filtered-out zone was still clickable.");
            IReadOnlyList<TriggerVolume>? overlaps = null;
            void CaptureProbe(Vector3 point, IReadOnlyList<TriggerVolume> inside) => overlaps = inside;
            ProbeChanged += CaptureProbe;
            try
            {
                ProbePoint = zone.Center;
                UpdateProbe();
                if (overlaps == null || overlaps.Count != 0)
                    throw new InvalidOperationException("Filtered-out zone still reported a probe overlap.");
            }
            finally { ProbeChanged -= CaptureProbe; ResetProbe(); }
        }
        finally { Size = oldSize; SelectedZonesOnly = false; ZoneSearch = ""; ShowAreaExtents = shown; }
    }
    internal void CheckPicking()
    {
        bool markers = ShowMarkers, candidates = ShowCandidates, extents = PickExtents, triggers = PickTriggers, zones = ShowAreaExtents;
        var oldSize = Size;
        try
        {
            ShowMarkers = ShowCandidates = PickExtents = false;
            Size = new Size(800, 600);
            Point? point = null;
            Actor? actor = null;
            for (int y = 80; y < 550 && actor == null; y += 65)
                for (int x = 80; x < 750 && actor == null; x += 65)
                {
                    var p = new Point(x, y);
                    actor = PickAt(p);
                    if (actor != null)
                        point = p;
                }
            if (actor == null || point == null)
                throw new InvalidOperationException("No visible mesh could be picked with markers disabled.");
            Actor? received = null;
            void CapturePick(Actor? a) => received = a;
            ActorPicked += CapturePick;
            try
            {
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.Value.X, point.Value.Y, 0));
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.Value.X, point.Value.Y, 0));
                if (!ReferenceEquals(actor, received))
                    throw new InvalidOperationException("Viewport click did not select the ray hit.");
            }
            finally { ActorPicked -= CapturePick; }
            PickTriggers = true;
            ShowAreaExtents = false;
            ZoneSearch = "__no_zone_matches__";
            ShowMarkers = ShowCandidates = true;
            if (PickAt(point.Value) != null)
                throw new InvalidOperationException("Zone-only click fell back to a visible model or marker.");
            PickTriggers = false;
            ZoneSearch = "";
            ShowAreaExtents = zones;
            ShowMarkers = ShowCandidates = false;
            SetNameFilter("__no_actor_matches__", true);
            if (PickAt(point.Value) != null)
                throw new InvalidOperationException("Hidden objects remained pickable.");
            SetNameFilter("", false);
        }
        finally { ShowMarkers = markers; ShowCandidates = candidates; PickExtents = extents; PickTriggers = triggers; ShowAreaExtents = zones; Size = oldSize; }
    }
    internal void CheckMovement()
    {
        var before = center;
        var savedKeys = movementKeys;
        var oldYaw = yaw;
        var oldPitch = pitch;
        var oldSpeed = flySpeed;
        try
        {
            movementKeys = MovementBit(Keys.W);
            AdvanceMovement(0.1f, false);
            var forward = Forward();
            if (Vector3.Dot(center - before, forward) <= 0 || Vector3.Cross(center - before, forward).Length() > 0.001f)
                throw new InvalidOperationException("WASD forward movement failed.");
            var moved = center;
            movementKeys = MovementBit(Keys.W) | MovementBit(Keys.S);
            AdvanceMovement(0.1f, false);
            if (center != moved)
                throw new InvalidOperationException("Opposite movement keys did not cancel.");
            movementKeys = MovementBit(Keys.D);
            AdvanceMovement(0.1f, false);
            var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
            if (Vector3.Dot(center - moved, right) <= 0)
                throw new InvalidOperationException("WASD strafe movement failed.");
            moved = center;
            movementKeys = MovementBit(Keys.E);
            AdvanceMovement(0.1f, false);
            if (center.Y <= moved.Y)
                throw new InvalidOperationException("Vertical flight failed.");
            var eye = CameraEye();
            LookRelative(12, -8);
            if (Vector3.Distance(eye, CameraEye()) > 0.001f)
                throw new InvalidOperationException("Looking around moved the camera.");
            var speedStart = center;
            movementKeys = MovementBit(Keys.W);
            AdvanceMovement(0.1f, false);
            float normalTravel = Vector3.Distance(speedStart, center);
            center = speedStart;
            AdvanceMovement(0.1f, true);
            if (Math.Abs(Vector3.Distance(speedStart, center) / normalTravel - 5) > 0.01f)
                throw new InvalidOperationException("Shift speed multiplier failed.");
            center = speedStart;
            AdvanceMovement(0.1f, false, true);
            if (Math.Abs(Vector3.Distance(speedStart, center) / normalTravel - 0.2f) > 0.01f)
                throw new InvalidOperationException("Ctrl speed multiplier failed.");
            eye = CameraEye();
            OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120));
            if (flySpeed >= oldSpeed || Vector3.Distance(eye, CameraEye()) > 0.001f)
                throw new InvalidOperationException("Wheel speed adjustment moved the camera.");
            if (MovementBit(Keys.R) != 32 || MovementBit(Keys.F) != 16)
                throw new InvalidOperationException("CodeWalker vertical bindings failed.");
            foreach (var button in new[] { MouseButtons.Left, MouseButtons.Right })
            {
                eye = CameraEye();
                var oldForward = Forward();
                OnMouseDown(new MouseEventArgs(button, 1, 10, 10, 0));
                OnMouseMove(new MouseEventArgs(button, 1, 25, 17, 0));
                OnMouseUp(new MouseEventArgs(button, 1, 25, 17, 0));
                if (Vector3.Distance(eye, CameraEye()) > 0.001f || Vector3.Distance(oldForward, Forward()) < 0.001f)
                    throw new InvalidOperationException($"{button} drag did not look around in place.");
            }
            movementKeys = 0;
            OnKeyDown(new KeyEventArgs(Keys.W));
            if (!movementTimer.Enabled || movementKeys != MovementBit(Keys.W))
                throw new InvalidOperationException("WASD key press did not start movement.");
            OnKeyUp(new KeyEventArgs(Keys.W));
            if (movementTimer.Enabled || movementKeys != 0)
                throw new InvalidOperationException("WASD key release did not stop movement.");
            OnKeyDown(new KeyEventArgs(Keys.A));
            OnLostFocus(EventArgs.Empty);
            if (movementTimer.Enabled || movementKeys != 0)
                throw new InvalidOperationException("Losing focus did not stop movement.");
        }
        finally { movementTimer.Stop(); movementKeys = savedKeys; center = before; yaw = oldYaw; pitch = oldPitch; flySpeed = oldSpeed; }
    }
}
