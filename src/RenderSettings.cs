using System.Numerics;
using System.Runtime.InteropServices;

namespace OotSceneViewer;

sealed class RenderSettings
{
    internal bool Lighting = true, Fog = true, Wireframe;
    internal bool Water = true, AnimateWater = true;
    internal bool Emission = true;
    internal bool Sky = true;
    internal float Clouds = 0.45f;
    internal float EmissionStrength = 1;
    internal float WaterRipples = 0.18f, WaterOpacity = 0.72f, WaterSpeed = 1;
    internal float SunAzimuth = 135, SunElevation = 45, SunStrength = 1.4f;
    internal float Ambient = 0.55f, Exposure = 1.15f, FogDistance = 2500;
    internal Vector3 SkyColor = new(0.24f, 0.34f, 0.48f);
    internal LightingConstants Constants(Vector3 eye, float time = 0)
    {
        float az = SunAzimuth * MathF.PI / 180, el = SunElevation * MathF.PI / 180;
        return new()
        {
            Sun = new(MathF.Cos(el) * MathF.Sin(az), MathF.Sin(el), MathF.Cos(el) * MathF.Cos(az), SunStrength),
            AmbientExposure = new(Ambient, Exposure, Lighting ? 1 : 0, Fog ? 1 : 0),
            Camera = new(eye, Math.Max(1, FogDistance)),
            Sky = new(SkyColor, 1),
            Water = new(time, WaterRipples, WaterOpacity, WaterSpeed)
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
struct LightingConstants
{
    public Vector4 Sun, AmbientExposure, Camera, Sky, Water;
}

[StructLayout(LayoutKind.Sequential)]
struct SkyConstants
{
    public Matrix4x4 InverseViewProjection;
    public Vector4 Options;
}
