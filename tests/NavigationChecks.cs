using System.Numerics;
using System.Text.Json;
using SpaceUI;

namespace OotSceneViewer;

public partial class Form1
{
    internal void CheckSelectionNavigation()
    {
        static Actor Make(string json, Vector3 position)
        {
            using var document = JsonDocument.Parse(json);
            var raw = document.RootElement.Clone();
            return new(raw.GetProperty("Gyaml").GetString()!, position, Vector3.Zero, Vector3.One, "test", raw);
        }
        var zone = Make("""{"Gyaml":"TestArea","Hash":1,"Scale":[1,1,1],"Links":[{"Dst":2,"Name":"Reference"},{"Dst":99,"Name":"Missing"}]}""", Vector3.Zero);
        var target = Make("""{"Gyaml":"EventRequestActor","Hash":2,"Dynamic":{"RequestEventName":"Test.Enter"}}""", new(50, 10, 40));
        actors = [zone, target];
        graph = new ActorGraph(actors);
        view.SuppressRendering = true;
        view.SetActors(actors, null, graph);
        RefreshTree();
        SelectFromViewport(zone);
        pickZones.Checked = true;
        details.ShowTriggerTab();
        view.FocusActor(zone);
        var original = view.CaptureCamera();
        void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Require(selectionHistory.Count == 0 && !backSelectionMenu.Enabled, "First selection created a history entry.");
        details.SelectedTab = 3;
        static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        var list = Descendants(details).OfType<SpaceListBox>().Single();
        var follow = Descendants(details).OfType<SpaceButton>().Single(b => b.Text == "Follow selected link");
        list.SelectedIndex = 1;
        Require(!follow.Enabled, "Unresolved link offered navigation.");
        details.FollowSelected();
        Require(ReferenceEquals(view.Selected, zone) && selectionHistory.Count == 0, "Unresolved link changed selection.");
        list.SelectedIndex = 0;
        Require(follow.Enabled, "Resolved link did not offer navigation.");
        details.FollowSelected();
        Require(ReferenceEquals(view.Selected, target) && ReferenceEquals(view.FollowedFrom, zone) && !pickZones.Checked,
            "Follow did not select its target and switch from zone to object mode.");
        Require(view.CaptureCamera() != original && selectionHistory.Count == 1 && backSelectionMenu.Enabled,
            "Follow did not frame its target or duplicated its history entry.");
        Require(view.NavigationLines().Count() > 24, "Follow did not draw its reference connection.");
        view.ShowLinks = false;
        Require(view.NavigationLines().Count() == 24, "Link display toggle hid the target marker or retained the connection.");
        view.ShowLinks = true;
        var destinationCamera = view.CaptureCamera();
        FollowActor(target);
        Require(selectionHistory.Count == 1, "Self-navigation duplicated history.");
        FollowActor(zone);
        Require(selectionHistory.Count == 2 && pickZones.Checked, "Following a zone did not restore zone mode.");
        BackSelection();
        Require(ReferenceEquals(view.Selected, target) && view.CaptureCamera() == destinationCamera && ReferenceEquals(view.FollowedFrom, zone),
            "Back lost a nested navigation view or reference path.");
        BackSelection();
        Require(ReferenceEquals(view.Selected, zone) && view.CaptureCamera() == original && pickZones.Checked && details.SelectedTab == 3 && view.FollowedFrom == null,
            "Back did not restore the source camera, mode and inspector tab.");
        Require(selectionHistory.Count == 0 && !backSelectionMenu.Enabled, "Back did not exhaust history.");
        SelectFromViewport(null);
        Require(backSelectionMenu.Enabled, "Clearing selection lost the previous selection.");
        BackSelection();
        Require(ReferenceEquals(view.Selected, zone), "Back from an empty selection failed.");
        workflowBusy = true;
        UpdateMenuState();
        FollowActor(target);
        Require(ReferenceEquals(view.Selected, zone) && !backSelectionMenu.Enabled, "Busy scene loading allowed navigation.");
        workflowBusy = false;
    }
}
