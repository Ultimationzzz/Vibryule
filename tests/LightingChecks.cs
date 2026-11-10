using System.Numerics;
using Vortice.Direct3D11;

namespace OotSceneViewer;

static class LightingChecks
{
    internal static string Run()
    {
        using var host = new Form { ClientSize = new Size(64, 64), ShowInTaskbar = false };
        using var renderer = new Direct3DRenderer(host.Handle);
        Vertex V(float x, float y, float z, float r, float g, float b, float a = 1) => new(x, y, z, r, g, b, a, 0, 0, 0, 1, 0);
        Vertex[] world = [V(-1, -1, 0.5f, 0.4f, 0.2f, 0.1f), V(-1, 1, 0.5f, 0.4f, 0.2f, 0.1f), V(1, -1, 0.5f, 0.4f, 0.2f, 0.1f)];
        Vertex[] overlay = [V(-1, -1, 0.8f, 0.1f, 0.2f, 1, 0.5f), V(-1, 1, 0.8f, 0.1f, 0.2f, 1, 0.5f), V(1, -1, 0.8f, 0.1f, 0.2f, 1, 0.5f)];
        renderer.SetWorld(world, [], [new(0, 3, 1)], []);
        var settings = new RenderSettings { Fog = false, Lighting = false, Exposure = 1 };
        int[] Draw(Vertex[]? zones = null, bool xray = true, float time = 0)
        {
            renderer.Draw(64, 64, Matrix4x4.Identity, [], 127, false, [], settings: settings, cameraPosition: new Vector3(0, 0, 20), zoneFaces: zones, zoneXray: xray, present: false, timeSeconds: time);
            return renderer.PixelForChecks(16, 40);
        }
        var baseline = Draw();
        settings.Exposure = 2;
        var bright = Draw();
        Require(bright.Take(3).Sum() > baseline.Take(3).Sum(), "Exposure did not affect the rendered surface.");
        settings.Exposure = 1;
        settings.Lighting = true;
        settings.Ambient = settings.SunStrength = 0;
        Require(Draw().Take(3).Sum() < baseline.Take(3).Sum(), "Lighting toggle/intensity did not affect the surface.");
        settings.Lighting = false;
        settings.Fog = true;
        settings.FogDistance = 1;
        Require(!Draw().SequenceEqual(baseline), "Distance haze did not change distant geometry.");
        settings.Fog = false;
        Require(Draw(overlay, false).SequenceEqual(baseline), "Depth-tested zone was drawn through foreground geometry.");
        Require(!Draw(overlay, true).SequenceEqual(baseline), "Through-wall translucent zone did not render.");
        Require(Draw().SequenceEqual(baseline), "Overlay blend/depth state leaked into the next frame.");
        settings.Wireframe = true;
        Require(!Draw().SequenceEqual(baseline), "Wireframe toggle did not change interior pixels.");
        settings.Wireframe = false;
        renderer.SetWorld(world, [], [new(0, 3, 1, Water: WaterKind.Surface)], []);
        var water = Draw();
        Require(!water.SequenceEqual(baseline), "Water shader did not replace the original material.");
        Require(!Draw(time: 1.5f).SequenceEqual(water), "Water animation did not change rendered pixels.");
        settings.AnimateWater = false;
        Require(Draw(time: 4).SequenceEqual(water), "Static water changed with time.");
        settings.Water = false;
        Require(Draw().SequenceEqual(baseline), "Disabling water did not restore the original material.");
        settings.Water = settings.AnimateWater = true;
        renderer.SetWorld(world, [], [new(0, 3, 1, Water: WaterKind.Waterfall)], []);
        Require(!Draw().SequenceEqual(Draw(time: 1.5f)), "Waterfall streaks did not animate.");
        // Opaque foreground must still occlude the transparent water pass.
        renderer.SetWorld(world.Concat(overlay).ToArray(), [], [new(0, 3, 1), new(3, 3, 1, Water: WaterKind.Surface)], []);
        Require(Draw().SequenceEqual(baseline), "Water was drawn through opaque foreground geometry.");
        Require(WaterMaterial.Classify("Land_WaterGate", "Gate__Mt_Cliff_ZoraRiver") == WaterKind.None &&
            WaterMaterial.Classify("Land_WaterTemple", "Wall__Mt_Rock_WaterTemple") == WaterKind.None &&
            WaterMaterial.Classify("River", "Surface__Mt_Water_River_Surface") == WaterKind.Surface &&
            WaterMaterial.Classify("River", "Box__Mt_Water_River_Box") == WaterKind.Volume,
            "Water detection classified scenery or failed to distinguish a volume proxy.");
        return "PASS: GPU pixels verify lighting, haze, wireframe, zone depth/x-ray, water animation/toggle/pause/occlusion and state reset.";
    }
    private static void Require(bool test, string message)
    {
        if (!test)
            throw new InvalidOperationException(message);
    }
}

sealed partial class Direct3DRenderer
{
    internal int[] PixelForChecks(int x, int y)
    {
        using var back = swap.GetBuffer<ID3D11Texture2D>(0);
        var description = back.Description;
        description.Usage = ResourceUsage.Staging;
        description.BindFlags = BindFlags.None;
        description.CPUAccessFlags = CpuAccessFlags.Read;
        description.MiscFlags = ResourceOptionFlags.None;
        using var read = device.CreateTexture2D(description);
        context.CopyResource(read, back);
        context.Map(read, 0, MapMode.Read, MapFlags.None, out var mapped).CheckError();
        try
        {
            var pixel = IntPtr.Add(mapped.DataPointer, checked(y * (int)mapped.RowPitch + x * 4));
            return Enumerable.Range(0, 4).Select(i => (int)System.Runtime.InteropServices.Marshal.ReadByte(pixel, i)).ToArray();
        }
        finally { context.Unmap(read, 0); }
    }
}
