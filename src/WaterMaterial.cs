namespace OotSceneViewer;

enum WaterKind
{
    None, Surface, Waterfall, Volume
}

static class WaterMaterial
{
    // Names come from the local mesh/material records. This is a viewer effect,
    // not a reconstruction of the game's water material graph.
    internal static WaterKind Classify(string actor, string shape)
    {
        int separator = shape.LastIndexOf("__", StringComparison.Ordinal);
        string material = separator >= 0 ? shape[(separator + 2)..] : shape;
        if (material.Contains("Mt_WaterVolume", StringComparison.OrdinalIgnoreCase) ||
            (material.Contains("Mt_Water_", StringComparison.OrdinalIgnoreCase) && material.EndsWith("_Box", StringComparison.OrdinalIgnoreCase)))
            return WaterKind.Volume;
        if (material.EndsWith("Mt_WaterFall", StringComparison.OrdinalIgnoreCase) || material.Contains("Mt_WaterFall_", StringComparison.OrdinalIgnoreCase))
            return WaterKind.Waterfall;
        if (material.Equals("Mt_Water", StringComparison.OrdinalIgnoreCase) || material.Contains("Mt_Water_", StringComparison.OrdinalIgnoreCase) ||
            (actor.Contains("WaterFall", StringComparison.OrdinalIgnoreCase) && material.Equals("Mt_Basin", StringComparison.OrdinalIgnoreCase)))
            return WaterKind.Surface;
        return WaterKind.None;
    }
}
