using System.Numerics;

namespace OotSceneViewer;

// Triangle picking uses the same world-space vertices as the renderer.
sealed class ScenePicking
{
    private readonly Vertex[] vertices;
    private readonly List<(SceneRange Range, Vector3 Min, Vector3 Max)> bounds = [];
    internal ScenePicking(Vertex[] vertices, SceneRange[] ranges)
    {
        this.vertices = vertices;
        foreach (var range in ranges)
        {
            if (range.Count == 0)
                continue;
            var min = new Vector3(float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity);
            for (uint i = range.Start; i < range.Start + range.Count; i++)
            {
                var p = Position(vertices[i]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            bounds.Add((range, min, max));
        }
    }
    internal Actor? Pick(Vector3 origin, Vector3 direction, Func<SceneRange, bool> visible, Func<Actor, bool> selectable)
    {
        float nearest = float.PositiveInfinity;
        Actor? selected = null;
        var candidates = new List<(SceneRange Range, float Near)>();
        foreach (var entry in bounds)
            if (visible(entry.Range) && Box(origin, direction, entry.Min, entry.Max, out float near, out _))
                candidates.Add((entry.Range, near));
        foreach (var candidate in candidates.OrderBy(c => c.Near))
        {
            if (candidate.Near > nearest)
                break;
            var range = candidate.Range;
            for (uint i = range.Start; i + 2 < range.Start + range.Count; i += 3)
                if (Triangle(origin, direction, Position(vertices[i]), Position(vertices[i + 1]), Position(vertices[i + 2]), out float distance) && distance < nearest)
                {
                    nearest = distance;
                    selected = range.Owner is Actor actor && selectable(actor) ? actor : null;
                }
        }
        return selected;
    }
    internal static Vector3 Position(Vertex v) => new(v.X, v.Y, v.Z);
    internal static bool Ray(Point pixel, Size viewport, Matrix4x4 viewProjection, out Vector3 origin, out Vector3 direction)
    {
        origin = direction = default;
        if (viewport.Width <= 0 || viewport.Height <= 0 || !Matrix4x4.Invert(viewProjection, out var inverse))
            return false;
        float x = 2f * pixel.X / viewport.Width - 1, y = 1 - 2f * pixel.Y / viewport.Height;
        var near = Vector4.Transform(new Vector4(x, y, 0, 1), inverse);
        var far = Vector4.Transform(new Vector4(x, y, 1, 1), inverse);
        if (Math.Abs(near.W) < 1e-9f || Math.Abs(far.W) < 1e-9f)
            return false;
        origin = new(near.X / near.W, near.Y / near.W, near.Z / near.W);
        var end = new Vector3(far.X / far.W, far.Y / far.W, far.Z / far.W);
        direction = Vector3.Normalize(end - origin);
        return float.IsFinite(direction.LengthSquared());
    }
    internal static bool Box(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, out float near, out float far)
    {
        near = 0;
        far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = direction[axis];
            if (Math.Abs(d) < 1e-8f)
            {
                if (o < min[axis] || o > max[axis])
                    return false;
                continue;
            }
            float a = (min[axis] - o) / d, b = (max[axis] - o) / d;
            if (a > b)
                (a, b) = (b, a);
            near = Math.Max(near, a);
            far = Math.Min(far, b);
            if (near > far)
                return false;
        }
        return far >= 0;
    }
    internal static bool Triangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
    {
        distance = 0;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(direction, e2);
        float det = Vector3.Dot(e1, p);
        if (Math.Abs(det) < 1e-8f)
            return false;
        float inv = 1 / det;
        var t = origin - a;
        float u = Vector3.Dot(t, p) * inv;
        if (u < 0 || u > 1)
            return false;
        var q = Vector3.Cross(t, e1);
        float v = Vector3.Dot(direction, q) * inv;
        if (v < 0 || u + v > 1)
            return false;
        distance = Vector3.Dot(e2, q) * inv;
        return distance >= 0;
    }
}

static class ActorNameFilter
{
    internal static bool Matches(Actor actor, string filter)
    {
        var terms = filter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            return true;
        string name = actor.Raw.TryGetProperty("Name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() ?? "" : "";
        return terms.Any(t => actor.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || name.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
