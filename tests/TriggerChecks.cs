using System.Numerics;
using System.Text.Json;

namespace OotSceneViewer;

static class TriggerChecks
{
    internal static string Run()
    {
        using var raw = JsonDocument.Parse("""{"Gyaml":"TestArea","Hash":18446744073709551615,"Scale":[2,3,4]}""");
        var actor = new Actor("TestArea", new(10, 20, 30), new(0, 0.7f, 0), new(2, 3, 4), "test", raw.RootElement.Clone());
        using var docs = JsonDocument.Parse("""{"Phive/ShapeParam/Test.bgyml":{"Box":[{"Center":{"X":0,"Y":0.5,"Z":0},"HalfExtents":{"X":0.5,"Y":0.5,"Z":0.5}}]}}""");
        var box = TriggerVolume.Read(actor, docs.RootElement).Single();
        Require(box.Evidence == ZoneEvidence.Primitive && box.Contains(box.Center), "Primitive box or its transformed center was lost.");
        Require(!box.Contains(Vector3.Transform(new(1.01f, 0, 0), box.Transform)), "Outside point was inside a rotated/scaled box.");
        var origin = Vector3.Transform(new(0, 0, -3), box.Transform);
        var direction = Vector3.Normalize(box.Center - origin);
        Require(box.Ray(origin, direction, out float hit) && hit > 0, "Rotated box ray missed.");
        using var sphereDocs = JsonDocument.Parse("""{"Phive/ShapeParam/Test.bgyml":{"Sphere":[{"Radius":1}]}}""");
        var sphere = TriggerVolume.Read(actor, sphereDocs.RootElement).Single();
        Require(sphere.Shape == ZoneGeometry.Sphere && sphere.Contains(actor.Position), "Resource sphere was not decoded.");
        Require(!sphere.Contains(Vector3.Transform(new(0.8f, 0.8f, 0), sphere.Transform)), "Sphere was tested as its bounding box.");
        Require(sphere.Ray(origin, direction, out _), "Ellipsoid ray missed.");
        using var unitBox = JsonDocument.Parse("""{"Phive/ShapeParam/Test.bgyml":{"Box":[{"HalfExtents":{"X":1,"Y":1,"Z":1}}]}}""");
        TriggerVolume Box(Vector3 position, Vector3 scale, Vector3 rotation = default) => TriggerVolume.Read(actor with
        {
            Position = position,
            Scale = scale,
            Rotation = rotation
        }, unitBox.RootElement).Single();
        var outer = Box(new(0, 0, 10), new(8));
        var inner = Box(new(0, 0, 10), Vector3.One);
        Require(ReferenceEquals(TriggerVolume.PickSmallest([outer, inner], Vector3.Zero, Vector3.UnitZ), inner) &&
            ReferenceEquals(TriggerVolume.PickSmallest([inner, outer], Vector3.Zero, Vector3.UnitZ), inner), "Enclosing zone intercepted the smaller nested zone.");
        var nearEqual = Box(new(0, 0, 5), Vector3.One);
        Require(ReferenceEquals(TriggerVolume.PickSmallest([inner, nearEqual], Vector3.Zero, Vector3.UnitZ), nearEqual), "Equal-volume zones did not use the nearest ray hit.");
        var rotatedEqual = Box(new(0, 0, 5), Vector3.One, new(0.1f, 0.2f, 0.3f));
        Require(ReferenceEquals(TriggerVolume.PickSmallest([inner, rotatedEqual], Vector3.Zero, Vector3.UnitZ), rotatedEqual), "Rotation rounding changed equal-volume priority.");
        var thinRotated = Box(new(0, 0, 10), new(-4, 0.1f, 0.1f), new(0, 0, 0.7f));
        Require(thinRotated.WorldVolume > 0 && thinRotated.WorldVolume < inner.WorldVolume &&
            ReferenceEquals(TriggerVolume.PickSmallest([inner, thinRotated], Vector3.Zero, Vector3.UnitZ), thinRotated), "Rotated or mirrored zone size used its bounding radius instead of volume.");
        var smallSphere = TriggerVolume.Read(actor with
        {
            Position = new(0, 0, 10),
            Scale = Vector3.One,
            Rotation = Vector3.Zero
        }, sphereDocs.RootElement).Single();
        Require(ReferenceEquals(TriggerVolume.PickSmallest([inner, smallSphere], Vector3.Zero, Vector3.UnitZ), smallSphere), "Sphere/box volumes were not compared consistently.");
        var missed = Box(new(20, 0, 10), new(0.01f));
        Require(ReferenceEquals(TriggerVolume.PickSmallest([missed, outer, inner], Vector3.Zero, Vector3.UnitZ), inner) &&
            TriggerVolume.PickSmallest([missed], Vector3.Zero, Vector3.UnitZ) == null, "Small zones outside the click ray affected picking.");
        using var bounds = JsonDocument.Parse("""{"Phive/ShapeParam/Test.bgyml":{"AutoCalc":{"Min":{"X":-1,"Y":0,"Z":-1},"Max":{"X":1,"Y":2,"Z":1}},"Cylinder":[{}]}}""");
        Require(TriggerVolume.Read(actor, bounds.RootElement).Single().Evidence == ZoneEvidence.ResourceBounds, "Unknown cylinder was mislabeled as an exact primitive.");
        Require(TriggerVolume.Read(actor, null).Single().Evidence == ZoneEvidence.Approximate, "Missing shapes were mislabeled as decoded collision.");
        Require(TriggerVolume.Read(actor with
        {
            Scale = Vector3.Zero
        }, null).Count == 0, "Singular volume was accepted.");
        using var peerRaw = JsonDocument.Parse("""{"Gyaml":"EventRequestActor","Hash":9223372036854775809,"Dynamic":{"RequestEventName":"Test.Enter"}}""");
        var peer = new Actor("EventRequestActor", Vector3.Zero, Vector3.Zero, Vector3.One, "test", peerRaw.RootElement.Clone());
        using var group = JsonDocument.Parse("""{"Logic":"Logic/Module/Test.ain","References":[{"Id":"Area","Reference":18446744073709551615},{"Id":"Event","Reference":9223372036854775809}]}""");
        var graph = new ActorGraph([actor, peer], logicGroups: [group.RootElement]);
        Require(ReferenceEquals(graph.Groups(actor).Single().References[1].Actor, peer), "UInt64 AI references did not resolve.");
        Require(graph.Links(actor).Count == 0, "AI co-membership was incorrectly treated as a causal placement link.");
        string Flatten(TreeNode node) => node.Text + string.Join("", node.Nodes.Cast<TreeNode>().Select(Flatten));
        var summary = string.Join("", TriggerInfo.Nodes(actor, graph).Select(Flatten));
        Require(summary.Contains("Test.Enter") && summary.Contains("execution order") && summary.Contains("approximate"), "Trigger summary omitted event evidence or uncertainty.");
        using var view = new SceneView { SuppressRendering = true, ShowAreaExtents = true };
        view.SetActors([actor], null);
        var overlaps = new List<int>();
        view.ProbeChanged += (_, inside) => overlaps.Add(inside.Count);
        view.ProbePoint = actor.Position + new Vector3(1000);
        view.UpdateProbe();
        view.ProbePoint = actor.Position;
        view.UpdateProbe();
        view.UpdateProbe(); // Remaining inside must not repeat an entry.
        view.ProbePoint = actor.Position + new Vector3(1000);
        view.UpdateProbe();
        Require(overlaps.SequenceEqual(new[] { 0, 1, 0 }), "Probe entry/exit transitions were lost or duplicated.");
        view.ResetProbe();
        view.ProbePoint = actor.Position + new Vector3(1000);
        view.UpdateProbe();
        Require(overlaps.Count == 4, "Restarting the probe at the same point did not refresh its state.");
        view.ProbePoint = actor.Position;
        view.ShowApproximate = false;
        view.UpdateProbe();
        Require(overlaps[^1] == 0, "Hidden approximate volumes remained in the probe results.");
        using var fallGroup = JsonDocument.Parse("""{"Logic":"Logic/Module/Gimmick_AreaContact_FallDown/Test.ain","References":[{"Id":"Area","Reference":18446744073709551615}]}""");
        var fallGraph = new ActorGraph([actor, peer], logicGroups: [fallGroup.RootElement]);
        Require(fallGraph.FallKind(actor) == FallZoneKind.FallContact && fallGraph.FallKind(peer) == FallZoneKind.None, "Fall classification spread to an unrelated actor.");
        Require(ActorGraph.ClassifyFallModule("Logic/Module/Gimmick_AreaContact_FallDown_Item/Test.ain") == FallZoneKind.ItemFall &&
            ActorGraph.ClassifyFallModule("Logic/Module/SwapRestartPos/Test.ain") == FallZoneKind.None &&
            ActorGraph.ClassifyFallModule("Logic/Module/DeathMountain/Test.ain") == FallZoneKind.None, "Item fall or unrelated modules were mislabeled as death/fall candidates.");
        view.SetActors([actor], null, fallGraph);
        view.ShowApproximate = true;
        var fallVolume = view.Zones.Single();
        Require(view.ZoneVisible(fallVolume), "Classified fall volume was not visible by default.");
        view.ShowFallZones = false;
        Require(!view.ZoneVisible(fallVolume), "Fall visibility toggle did not exclude the map volume.");
        view.ShowFallZones = true;
        view.OnlyFallZones = true;
        Require(view.ZoneVisible(fallVolume) && !view.ZoneMatches(TriggerVolume.Read(actor, null).Single()), "Fall-only filter included an unclassified zone.");
        return "PASS: resource primitives, transformed bounds, sphere containment/rays, approximate fallback, singular transforms, UInt64 logic references and probe entry/exit/reset.";
    }
    private static void Require(bool test, string message)
    {
        if (!test)
            throw new InvalidOperationException(message);
    }
}
