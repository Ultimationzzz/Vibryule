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
        int[] Draw(Vertex[]? zones = null, bool xray = true, float time = 0, bool gameRendering = true)
        {
            renderer.Draw(64, 64, Matrix4x4.Identity, [], 127, false, [], gameRendering: gameRendering, settings: settings, cameraPosition: new Vector3(0, 0, 20), zoneFaces: zones, zoneXray: xray, present: false, timeSeconds: time);
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
        Require(WaterMaterial.Classify("Land_HrlCastle_WaterPlane_A_01", "WaterPlane_Front__Mt_HrlCastle_WaterPlane_A_01_Front") == WaterKind.Surface &&
            WaterMaterial.Classify("Land_HrlCastle_WaterPlane_A_01", "WaterPlane_Back__Mt_HrlCastle_WaterPlane_A_01_Back") == WaterKind.Backface &&
            WaterMaterial.Classify("Land_HrlCastle_WaterPlane_A_01", "WaterPlane_Box__Mt_HrlCastle_WaterPlane_A_01_Box") == WaterKind.Volume &&
            WaterMaterial.Classify("Land_HrlCastle_WaterPlane_A_01", "WaterPlane_Surface__Mt_HrlCastle_WaterPlane_A_01_Model") == WaterKind.Volume &&
            WaterMaterial.Classify("Land_HrlCastle_Waterfall_A_01", "WaterFallMaker_B_00_0__Mt_Seal_Top") == WaterKind.Surface,
            "Castle water parts were not classified separately from auxiliary meshes.");
        renderer.SetWorld(world, [], [new(0, 3, 1, Water: WaterKind.Surface)], []);
        var oneWaterLayer = Draw();
        renderer.SetWorld(world.Concat(world).ToArray(), [], [new(0, 3, 1, Water: WaterKind.Surface), new(3, 3, 1, Water: WaterKind.Backface)], []);
        Require(Draw().SequenceEqual(oneWaterLayer), "Companion water backface was blended twice.");
        // Self-lit material must remain visible without any sun or ambient light.
        string emissionPath = Path.Combine(Path.GetTempPath(), $"vibryule-emission-{Guid.NewGuid():N}.tex");
        string albedoPath = Path.Combine(Path.GetTempPath(), $"vibryule-albedo-{Guid.NewGuid():N}.tex");
        try
        {
            new BntxTexture(1, 1, Vortice.DXGI.Format.R8G8B8A8_UNorm_SRgb, 4, [255, 0, 0, 255]).Save(emissionPath);
            new BntxTexture(1, 1, Vortice.DXGI.Format.R8G8B8A8_UNorm_SRgb, 4, [32, 32, 32, 255]).Save(albedoPath);
            settings.Lighting = true;
            settings.SunStrength = settings.Ambient = 0;
            settings.Emission = true;
            settings.EmissionStrength = 0.5f;
            renderer.SetWorld(world, [], [new(0, 3, 1, Texture: albedoPath, EmissionTexture: emissionPath)], []);
            var emitted = Draw();
            Require(emitted[0] > 100 && emitted[1] < 5 && emitted[2] < 5, "Emission texture did not retain its color in darkness.");
            settings.EmissionStrength = 2;
            Require(Draw()[0] > emitted[0], "Emission brightness did not affect the material.");
            settings.Emission = false;
            Require(Draw().Take(3).Sum() < 5, "Emission toggle did not restore the unlit surface.");
            settings.Emission = true;
            Require(Draw(gameRendering: false).Take(3).Sum() < 5, "Solid inspection mode still emitted light.");
            var unfogged = Draw();
            settings.Fog = true;
            settings.FogDistance = 1;
            Require(!Draw().SequenceEqual(unfogged), "Emission bypassed distance haze.");
            settings.Fog = false;
            renderer.SetWorld(world, [], [new(0, 3, 1, EmissionTexture: emissionPath)], []);
            Require(Draw().SequenceEqual(unfogged), "An emission map without albedo failed to render.");
            // Draw a closer non-emissive range after the emitting range, in the same frame.
            renderer.SetWorld(overlay.Concat(world).ToArray(), [], [new(0, 3, 1, EmissionTexture: emissionPath), new(3, 3, 1)], []);
            Require(Draw().Take(3).Sum() < 5, "Emission leaked into the next material.");
        }
        finally { File.Delete(emissionPath); File.Delete(albedoPath); }
        renderer.SetWorld([], [], [], []);
        settings.Clouds = 0;
        int[] Sky(Vector3 direction, Vector3 eye = default)
        {
            var matrix = Matrix4x4.CreateLookAt(eye, eye + direction, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(0.8f, 1, 0.1f, 10000);
            renderer.Draw(64, 64, matrix, [], 127, false, [], settings: settings, cameraPosition: eye, present: false);
            return renderer.PixelForChecks(32, 32);
        }
        var horizon = Sky(new(0, 0.01f, -1));
        var upper = Sky(new(0, 2, -1));
        Require(!horizon.SequenceEqual(upper), "Sky remained fixed to the screen when pitching the camera.");
        var translated = Sky(new(0, 2, -1), new(100, 20, 30));
        Require(upper.Zip(translated).All(p => Math.Abs(p.First - p.Second) <= 2), "Sky translated with world position.");
        settings.Clouds = 1;
        Require(!Sky(new(0, 2, -1)).SequenceEqual(upper), "Cloud cover did not change the sky.");
        settings.Sky = false;
        Require(Sky(new(0, 0.01f, -1)).SequenceEqual(Sky(new(0, 2, -1))), "Sky toggle did not restore a plain background.");
        return "PASS: GPU pixels verify sky pitch/translation/cloud controls, water layering/animation/occlusion, emission, lighting and state reset.";
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
