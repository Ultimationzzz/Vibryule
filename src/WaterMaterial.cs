namespace OotSceneViewer;

enum WaterKind
{
    None, Surface, Waterfall, Volume, Backface
}

static class WaterMaterial
{
    // Names come from the local mesh/material records. This is a viewer effect,
    // not a reconstruction of the game's water material graph.
    internal static WaterKind Classify(string actor, string shape)
    {
        int separator = shape.LastIndexOf("__", StringComparison.Ordinal);
        string material = separator >= 0 ? shape[(separator + 2)..] : shape;
        bool waterPlane = material.Contains("WaterPlane", StringComparison.OrdinalIgnoreCase);
        bool water = waterPlane || material.Contains("Mt_Water_", StringComparison.OrdinalIgnoreCase);
        // The renderer is two-sided. Rendering the companion back mesh too causes
        // coplanar alpha layers and interference patterns on the same surface.
        if (water && material.Contains("_Back", StringComparison.OrdinalIgnoreCase))
            return WaterKind.Backface;
        if (material.Contains("Mt_WaterVolume", StringComparison.OrdinalIgnoreCase) ||
            (water && material.EndsWith("_Box", StringComparison.OrdinalIgnoreCase)) ||
            (waterPlane && material.EndsWith("_Model", StringComparison.OrdinalIgnoreCase)))
            return WaterKind.Volume;
        if (material.EndsWith("Mt_WaterFall", StringComparison.OrdinalIgnoreCase) || material.Contains("Mt_WaterFall_", StringComparison.OrdinalIgnoreCase))
            return WaterKind.Waterfall;
        if (material.Equals("Mt_Water", StringComparison.OrdinalIgnoreCase) || water ||
            (actor.Contains("WaterFall", StringComparison.OrdinalIgnoreCase) && (material.Equals("Mt_Basin", StringComparison.OrdinalIgnoreCase) || material.Equals("Mt_Seal_Top", StringComparison.OrdinalIgnoreCase))))
            return WaterKind.Surface;
        return WaterKind.None;
    }
}
