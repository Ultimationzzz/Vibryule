using System.Numerics;

namespace OotSceneViewer;

public partial class Form1
{
    private record SelectionVisit(Actor Actor, CameraView Camera, bool Zones, bool Extents, int Tab, Actor? FollowedFrom);
    private readonly List<SelectionVisit> selectionHistory = [];
    private bool restoringSelection;

    private void RememberSelection(Actor? next)
    {
        if (restoringSelection || ReferenceEquals(next, view.Selected))
            return;
        if (view.Selected is Actor previous)
        {
            selectionHistory.Add(new(previous, view.CaptureCamera(), pickZones.Checked, pickExtents.Checked, details.SelectedTab, view.FollowedFrom));
            if (selectionHistory.Count > 128)
                selectionHistory.RemoveAt(0);
        }
        view.FollowedFrom = null;
    }

    private void FollowActor(Actor target)
    {
        if (workflowBusy || !actors.Any(a => ReferenceEquals(a, target)) || ReferenceEquals(target, view.Selected))
            return;
        var source = view.Selected;
        SelectFromViewport(target);
        pickZones.Checked = view.Zones.Any(z => ReferenceEquals(z.Actor, target));
        pickExtents.Checked = false;
        view.FollowedFrom = source;
        view.FocusActor(target);
        sidebarTabs.SelectedIndex = 0;
        details.ShowTriggerTab();
        status.Text = $"Following {source?.Name ?? "reference"} → {target.Name}. Reference connection only; execution order unknown. Visibility filters retained. Alt+Left returns.";
    }

    private void BackSelection()
    {
        if (workflowBusy || selectionHistory.Count == 0)
            return;
        var previous = selectionHistory[^1];
        selectionHistory.RemoveAt(selectionHistory.Count - 1);
        restoringSelection = true;
        try
        {
            SelectFromViewport(previous.Actor);
            pickZones.Checked = previous.Zones;
            pickExtents.Checked = previous.Extents;
            view.FollowedFrom = previous.FollowedFrom;
            view.RestoreCamera(previous.Camera);
            details.SelectedTab = previous.Tab;
            sidebarTabs.SelectedIndex = 0;
            status.Text = "Returned to " + previous.Actor.Name;
        }
        finally
        {
            restoringSelection = false;
            UpdateMenuState();
        }
    }
}

record struct CameraView(Vector3 Center, float Distance, float Yaw, float Pitch);

partial class SceneView
{
    internal Actor? FollowedFrom;
    internal CameraView CaptureCamera() => new(center, distance, yaw, pitch);
    internal void RestoreCamera(CameraView camera)
    {
        center = camera.Center;
        distance = camera.Distance;
        yaw = camera.Yaw;
        pitch = camera.Pitch;
        Invalidate();
    }

    internal IEnumerable<Vertex> NavigationLines()
    {
        if (FollowedFrom == null || Selected == null)
            yield break;
        // Keep a destination marker visible even when filters hide its geometry.
        foreach (var vertex in BoxLines(Selected.Position - new Vector3(0.5f), Selected.Position + new Vector3(0.5f), 1, 0.72f, 0.1f))
            yield return vertex;
        if (!ShowLinks)
            yield break;
        var a = FollowedFrom.Position;
        var b = Selected.Position;
        // Dashed path is a navigation reference, not a decoded execution edge.
        for (int i = 0; i < 32; i++)
        {
            var start = Vector3.Lerp(a, b, i / 32f);
            var end = Vector3.Lerp(a, b, (i + 0.55f) / 32f);
            yield return new(start.X, start.Y, start.Z, 0.3f, 0.85f, 1, 1);
            yield return new(end.X, end.Y, end.Z, 0.3f, 0.85f, 1, 1);
        }
    }
}
