using System.Numerics;
using System.Text.Json;

namespace OotSceneViewer;

enum ZoneGeometry
{
    Box, Sphere
}
enum ZoneEvidence
{
    Primitive, ResourceBounds, Approximate
}

// These are inspection volumes, not a replacement for the game's physics engine.
sealed class TriggerVolume
{
    internal Actor Actor
    {
        get;
    }
    internal ZoneGeometry Shape
    {
        get;
    }
    internal ZoneEvidence Evidence
    {
        get;
    }
    internal string Source
    {
        get;
    }
    internal Matrix4x4 Transform
    {
        get;
    }
    internal Matrix4x4 Inverse
    {
        get;
    }
    internal Vector3 Center => Vector3.Transform(Vector3.Zero, Transform);
    internal double WorldVolume => Math.Abs((double)Transform.GetDeterminant()) * (Shape == ZoneGeometry.Sphere ? 4 * Math.PI / 3 : 8);
    internal bool IsFog => Actor.Name.Equals("VolumeFogArea", StringComparison.OrdinalIgnoreCase);
    internal Vertex[] Lines
    {
        get;
    }
    internal Vertex[] Faces
    {
        get;
    }
    internal float Radius
    {
        get;
    }
    internal string Label => Evidence switch { ZoneEvidence.Primitive => "resource primitive", ZoneEvidence.ResourceBounds => "resource bounds", _ => "approximate" };
    internal FallZoneKind FallKind
    {
        get; private set;
    }

    private TriggerVolume(Actor actor, ZoneGeometry shape, ZoneEvidence evidence, string source, Matrix4x4 transform, Matrix4x4 inverse)
    {
        Actor = actor;
        Shape = shape;
        Evidence = evidence;
        Source = source;
        Transform = transform;
        Inverse = inverse;
        var color = evidence == ZoneEvidence.Approximate ? new Vector3(1, 0.62f, 0.2f) : new Vector3(0.2f, 0.82f, 0.96f);
        var lines = new List<Vertex>();
        var faces = new List<Vertex>();
        Vertex V(Vector3 local, float alpha)
        {
            var p = Vector3.Transform(local, transform);
            return new(p.X, p.Y, p.Z, color.X, color.Y, color.Z, alpha);
        }
        void Edge(Vector3 a, Vector3 b)
        {
            lines.Add(V(a, 0.9f));
            lines.Add(V(b, 0.9f));
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            faces.Add(V(a, 0.12f));
            faces.Add(V(b, 0.12f));
            faces.Add(V(c, 0.12f));
        }
        if (shape == ZoneGeometry.Box)
        {
            Vector3[] p = [new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1), new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)];
            int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
            for (int i = 0; i < edges.Length; i += 2)
                Edge(p[edges[i]], p[edges[i + 1]]);
            int[] corners = [0, 3, 2, 1, 4, 5, 6, 7, 0, 4, 7, 3, 1, 2, 6, 5, 3, 7, 6, 2, 0, 1, 5, 4];
            for (int i = 0; i < corners.Length; i += 4)
            {
                Triangle(p[corners[i]], p[corners[i + 1]], p[corners[i + 2]]);
                Triangle(p[corners[i]], p[corners[i + 2]], p[corners[i + 3]]);
            }
        }
        else
        {
            const int slices = 24, rings = 12;
            Vector3 Point(int ring, int slice)
            {
                float lat = -MathF.PI / 2 + MathF.PI * ring / rings, lon = 2 * MathF.PI * slice / slices;
                return new(MathF.Cos(lat) * MathF.Cos(lon), MathF.Sin(lat), MathF.Cos(lat) * MathF.Sin(lon));
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < slices; s++)
                {
                    var a = Point(r, s);
                    var b = Point(r, s + 1);
                    var c = Point(r + 1, s);
                    var d = Point(r + 1, s + 1);
                    Triangle(a, c, b);
                    Triangle(b, c, d);
                    if (r % 3 == 0)
                        Edge(a, b);
                    if (s % 6 == 0)
                        Edge(a, c);
                }
        }
        Lines = lines.ToArray();
        Faces = faces.ToArray();
        Radius = Lines.Select(v => Vector3.Distance(Center, new(v.X, v.Y, v.Z))).DefaultIfEmpty(1).Max();
    }

    internal bool Contains(Vector3 point)
    {
        var p = Vector3.Transform(point, Inverse);
        return Shape == ZoneGeometry.Sphere ? p.LengthSquared() <= 1 : Math.Abs(p.X) <= 1 && Math.Abs(p.Y) <= 1 && Math.Abs(p.Z) <= 1;
    }
    internal bool Ray(Vector3 origin, Vector3 direction, out float hit)
    {
        var o = Vector3.Transform(origin, Inverse);
        var d = Vector3.TransformNormal(direction, Inverse);
        hit = float.PositiveInfinity;
        if (Shape == ZoneGeometry.Box)
        {
            if (!ScenePicking.Box(o, d, -Vector3.One, Vector3.One, out float near, out float far))
                return false;
            hit = near > 0.001f ? near : far;
            return float.IsFinite(hit);
        }
        float a = Vector3.Dot(d, d), b = Vector3.Dot(o, d), c = Vector3.Dot(o, o) - 1, discriminant = b * b - a * c;
        if (a < 1e-12f || discriminant < 0)
            return false;
        float nearHit = (-b - MathF.Sqrt(discriminant)) / a, farHit = (-b + MathF.Sqrt(discriminant)) / a;
        hit = nearHit > 0.001f ? nearHit : farHit;
        return hit >= 0 && float.IsFinite(hit);
    }
    public override string ToString() => $"{Actor.Name} · {Center.X:F0}, {Center.Y:F0}, {Center.Z:F0} · {Label}";

    internal static TriggerVolume? PickSmallest(IEnumerable<TriggerVolume> zones, Vector3 origin, Vector3 direction)
    {
        TriggerVolume? selected = null;
        double smallest = double.PositiveInfinity;
        float nearest = float.PositiveInfinity;
        foreach (var zone in zones)
        {
            if (!zone.Ray(origin, direction, out float hit))
                continue;
            double volume = zone.WorldVolume;
            if (!double.IsFinite(volume) || volume <= 0)
                continue;
            // Rotation can introduce a tiny determinant rounding difference.
            bool sameSize = selected != null && Math.Abs(volume - smallest) <= Math.Max(volume, smallest) * 1e-6;
            if (selected == null || (!sameSize && volume < smallest) || (sameSize && hit < nearest))
            {
                selected = zone;
                smallest = volume;
                nearest = hit;
            }
        }
        return selected;
    }

    internal static IReadOnlyList<TriggerVolume> Read(Actor actor, JsonElement? definitions, FallZoneKind fallKind = FallZoneKind.None)
    {
        var result = new List<TriggerVolume>();
        if (!IsCandidate(actor) || !Finite(actor.Position) || !Finite(actor.Scale) || !Finite(actor.Rotation))
            return result;
        var placement = Matrix4x4.CreateScale(actor.Scale) * Matrix4x4.CreateRotationX(actor.Rotation.X) * Matrix4x4.CreateRotationY(actor.Rotation.Y) * Matrix4x4.CreateRotationZ(actor.Rotation.Z) * Matrix4x4.CreateTranslation(actor.Position);
        void Add(ZoneGeometry shape, ZoneEvidence evidence, string source, Vector3 center, Vector3 extent)
        {
            if (!Finite(center) || !Finite(extent) || extent.X <= 0 || extent.Y <= 0 || extent.Z <= 0)
                return;
            var transform = Matrix4x4.CreateScale(extent) * Matrix4x4.CreateTranslation(center) * placement;
            if (!Matrix4x4.Invert(transform, out var inverse) || !float.IsFinite(inverse.M11) || !float.IsFinite(inverse.M44))
                return;
            result.Add(new(actor, shape, evidence, source, transform, inverse)
            {
                FallKind = fallKind
            });
        }
        if (definitions is { ValueKind: JsonValueKind.Object } docs)
            foreach (var doc in docs.EnumerateObject())
            {
                if (!doc.Name.Contains("ShapeParam/", StringComparison.Ordinal) || doc.Value.ValueKind != JsonValueKind.Object)
                    continue;
                var root = doc.Value;
                // Bone-bound, inherited and offset shapes need further transform decoding.
                if (root.TryGetProperty("TrackingBoneName", out _) || root.TryGetProperty("$parent", out _))
                    continue;
                bool primitives = false;
                foreach (string type in new[] { "Box", "Sphere" })
                    if (root.TryGetProperty(type, out var shapes) && shapes.ValueKind == JsonValueKind.Array)
                        foreach (var shape in shapes.EnumerateArray())
                        {
                            if (shape.ValueKind != JsonValueKind.Object || shape.TryGetProperty("OffsetRotation", out _) || shape.TryGetProperty("OffsetTranslation", out _))
                                continue;
                            var center = Vector(shape, "Center") ?? Vector3.Zero;
                            if (type == "Box" && Vector(shape, "HalfExtents") is Vector3 extent)
                            {
                                Add(ZoneGeometry.Box, ZoneEvidence.Primitive, doc.Name, center, extent);
                                primitives = true;
                            }
                            if (type == "Sphere" && shape.TryGetProperty("Radius", out var radius) && radius.TryGetSingle(out float r))
                            {
                                Add(ZoneGeometry.Sphere, ZoneEvidence.Primitive, doc.Name, center, new Vector3(r));
                                primitives = true;
                            }
                        }
                if (!primitives && root.TryGetProperty("AutoCalc", out var auto) && Vector(auto, "Min") is Vector3 min && Vector(auto, "Max") is Vector3 max)
                    Add(ZoneGeometry.Box, ZoneEvidence.ResourceBounds, doc.Name + " / AutoCalc", (min + max) / 2, (max - min) / 2);
            }
        if (result.Count == 0 && actor.Raw.TryGetProperty("Scale", out _))
            Add(ZoneGeometry.Box, ZoneEvidence.Approximate, "Placement scale only; shape, pivot and runtime size unresolved", Vector3.Zero, Vector3.One);
        return result;
    }
    internal static bool IsCandidate(Actor actor) => actor.Name.Contains("Area", StringComparison.OrdinalIgnoreCase) || actor.Name.Contains("Trigger", StringComparison.OrdinalIgnoreCase);
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static Vector3? Vector(JsonElement parent, string key)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(key, out var p) || p.ValueKind != JsonValueKind.Object)
            return null;
        if (p.TryGetProperty("X", out var x) && p.TryGetProperty("Y", out var y) && p.TryGetProperty("Z", out var z) && x.TryGetSingle(out float a) && y.TryGetSingle(out float b) && z.TryGetSingle(out float c))
            return new(a, b, c);
        return null;
    }
}
