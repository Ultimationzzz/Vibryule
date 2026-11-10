using System.Numerics;
using System.Text.Json;

namespace OotSceneViewer;

static class SelectionChecks
{
    internal static string Run()
    {
        using var placement = JsonDocument.Parse("""{"Gyaml":"TestArea","Name":"Door trigger","Hash":18446744073709551615,"Dynamic":{"Enabled":true,"Flags":[true,false],"EventFlag":"Quest.Open"},"Presence":{"FlagName":"Door.Open","IsNegation":false}}""");
        using var defs = JsonDocument.Parse("""{"Component/Blackboard/BlackboardParamTable/Test.bgyml":{"BlackboardParamBoolArray":[{"BBKey":"Enabled","InitVal":false,"IsInstanceParam":true},{"BBKey":"ReadOnlyDefault","InitVal":true,"IsInstanceParam":false}],"OtherArray":[[1,2],[]]}}""");
        var a = new Actor("TestArea", Vector3.Zero, Vector3.Zero, Vector3.One, "test", placement.RootElement.Clone());
        var rows = ActorInspector.ReadFlags(a, defs.RootElement);
        var enabled = rows.Single(r => r.Name == "Enabled");
        Require(enabled.Bool == true && enabled.Default == "false" && enabled.Source.StartsWith("Placement override"), "Default/override distinction failed.");
        Require(rows.Any(r => r.Name == "Dynamic.Flags[0]" && r.Bool == true) && rows.Any(r => r.Name == "Dynamic.Flags[1]" && r.Bool == false), "Boolean array indices were lost.");
        Require(rows.Any(r => r.Name == "Presence.FlagName" && r.Bool == null && r.Value == "Door.Open"), "Flag identifier was treated as a runtime boolean.");
        var array = ActorInspector.ValueNode("OtherArray", defs.RootElement.EnumerateObject().First().Value.GetProperty("OtherArray"));
        Require(array.Nodes.Count == 2 && array.Nodes[0].Nodes.Count == 2 && array.Nodes[1].Nodes.Count == 0, "Nested/empty arrays did not preserve shape.");
        Require(ActorInspector.ValueNode("Hash", a.Raw.GetProperty("Hash")).Text.Contains("18446744073709551615"), "Unsigned identifier lost precision.");
        Require(ActorNameFilter.Matches(a, "Tree,door") && ActorNameFilter.Matches(a, "testarea") && !ActorNameFilter.Matches(a, "Rock"), "Name filtering failed.");
        var b = a with
        {
            Name = "FarObject"
        };
        Vertex[] vertices = [new(-1, -1, 2, 1, 1, 1, 1), new(1, -1, 2, 1, 1, 1, 1), new(0, 1, 2, 1, 1, 1, 1), new(-1, -1, 5, 1, 1, 1, 1), new(1, -1, 5, 1, 1, 1, 1), new(0, 1, 5, 1, 1, 1, 1)];
        SceneRange[] ranges = [new(0, 3, 1, Owner: a), new(3, 3, 1, Owner: b)];
        var picker = new ScenePicking(vertices, ranges);
        Require(ReferenceEquals(picker.Pick(Vector3.Zero, Vector3.UnitZ, _ => true, _ => true), a), "Nearest triangle was not selected.");
        Require(ReferenceEquals(picker.Pick(Vector3.Zero, Vector3.UnitZ, r => r.Owner != a, _ => true), b), "Hidden geometry blocked selection.");
        Require(picker.Pick(Vector3.Zero, Vector3.UnitZ, _ => true, x => x == b) == null, "Unselectable foreground allowed picking through it.");
        var terrain = new ScenePicking(vertices, [ranges[0] with { Owner = null, Terrain = true }, ranges[1]]);
        Require(terrain.Pick(Vector3.Zero, Vector3.UnitZ, _ => true, _ => true) == null, "Terrain did not occlude hidden objects.");
        Require(picker.Pick(new Vector3(0.95f, 0.9f, 0), Vector3.UnitZ, _ => true, _ => true) == null, "AABB empty space selected a triangle.");
        var transform = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(0, 0, 10);
        Matrix4x4.Invert(transform, out var inverse);
        Require(ScenePicking.Box(Vector3.Transform(Vector3.Zero, inverse), Vector3.TransformNormal(Vector3.UnitZ, inverse), -Vector3.One, Vector3.One, out var near, out _) && near > 0, "Rotated extent intersection failed.");
        var vp = Matrix4x4.CreateLookAt(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(0.8f, 1, 0.1f, 100);
        Require(ScenePicking.Ray(new Point(100, 100), new Size(200, 200), vp, out _, out var direction) && Vector3.Dot(direction, Vector3.UnitZ) > 0.999f, "Screen ray unprojection failed.");
        return "PASS: typed flags, overrides, nested/empty arrays, UInt64 IDs, OR name filters, nearest triangle picking, hidden geometry, terrain occlusion, rotated extents and screen rays.";
    }
    private static void Require(bool condition, string error)
    {
        if (!condition)
            throw new InvalidOperationException(error);
    }
}
