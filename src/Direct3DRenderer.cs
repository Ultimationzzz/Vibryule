using System.Runtime.InteropServices;
using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace OotSceneViewer;

[StructLayout(LayoutKind.Sequential)]
struct Vertex(float x, float y, float z, float r, float g, float b, float a, float u = 0, float v = 0, float nx = 0, float ny = 0, float nz = 0)
{
    public float X = x, Y = y, Z = z, R = r, G = g, B = b, A = a, U = u, V = v, Nx = nx, Ny = ny, Nz = nz;
}

sealed partial class Direct3DRenderer : IDisposable
{
    internal const string WorldShader = "cbuffer Camera : register(b0) { row_major float4x4 viewProjection; }; struct V { float3 p:POSITION; float4 c:COLOR; }; struct O { float4 p:SV_POSITION; float4 c:COLOR; }; O vs(V v){ O o; o.p=mul(float4(v.p,1),viewProjection); o.c=v.c; return o; }";
    internal const string TextureShader = """
        cbuffer Camera : register(b0) { row_major float4x4 viewProjection; };
        cbuffer Lighting : register(b1) { float4 sun; float4 ambientExposure; float4 camera; float4 skyColor; float4 water; };
        cbuffer Material : register(b2) { float4 emission; };
        cbuffer Sky : register(b3) { row_major float4x4 inverseViewProjection; float4 skyOptions; };
        Texture2D tex:register(t0); SamplerState samp:register(s0);
        Texture2D emissionTex:register(t1);
        struct V { float3 p:POSITION; float4 c:COLOR; float2 uv:TEXCOORD; float3 n:NORMAL; };
        struct O { float4 p:SV_POSITION; float4 c:COLOR; float2 uv:TEXCOORD0; float3 n:NORMAL; float3 world:TEXCOORD1; };
        O vs(V v) { O o; o.p=mul(float4(v.p,1),viewProjection); o.c=v.c; o.uv=v.uv; o.n=v.n; o.world=v.p; return o; }
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float noise(float2 p) {
            float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(hash(cell),hash(cell+float2(1,0)),f.x),lerp(hash(cell+float2(0,1)),hash(cell+1),f.x),f.y);
        }
        float cloudNoise(float2 p) {
            return noise(p)*0.53+noise(p*2.03+7.1)*0.27+noise(p*4.07+19.3)*0.13+noise(p*8.11)*0.07;
        }
        float3 skyRadiance(float3 ray) {
            if(skyOptions.x<0.5)return skyColor.rgb;
            float height=saturate(ray.y);
            float3 horizon=lerp(skyColor.rgb,float3(0.65,0.74,0.80),0.65);
            float3 zenith=skyColor.rgb*float3(0.32,0.56,1.0);
            float3 color=lerp(horizon,zenith,pow(height,0.45));
            color=lerp(color,skyColor.rgb*0.4,saturate(-ray.y*4));
            float sunDot=saturate(dot(ray,sun.xyz));
            float halo=pow(sunDot,32)*0.18;
            float disk=smoothstep(0.99965,0.9999,sunDot)*3;
            color+=float3(1.0,0.82,0.56)*(halo+disk)*min(sun.w,2)*smoothstep(-0.08,0.04,ray.y);
            if(skyOptions.y>0.001 && ray.y>0) {
                float2 p=ray.xz/max(ray.y,0.08)*1.5;
                float cloud=cloudNoise(p);
                float threshold=lerp(0.85,0.25,skyOptions.y);
                float coverage=smoothstep(threshold,threshold+0.16,cloud)*smoothstep(0.01,0.18,ray.y);
                float3 cloudColor=lerp(float3(0.46,0.55,0.66),float3(1.05,1.04,1.0),smoothstep(threshold,threshold+0.3,cloud));
                color=lerp(color,cloudColor,coverage*0.9);
            }
            return color;
        }
        float3 displayColor(float3 radiance) {
            // Soft highlight roll-off, followed by the linear-to-sRGB transfer.
            float3 mapped=1-exp(-max(radiance,0)*ambientExposure.y);
            return lerp(12.92*mapped,1.055*pow(max(mapped,0.000001),1.0/2.4)-0.055,step(0.0031308,mapped));
        }
        float4 shade(O i,float3 albedo,bool front) {
            float3 n=dot(i.n,i.n)>0.0001?normalize(i.n):float3(0,1,0);
            if(!front)n=-n;
            float3 radiance=albedo;
            if(ambientExposure.z>0.5) {
                float hemi=saturate(n.y*0.5+0.5);
                float3 ambient=lerp(float3(0.26,0.23,0.20),float3(0.62,0.73,0.90),hemi)*ambientExposure.x;
                float diffuse=saturate(dot(n,sun.xyz));
                radiance=albedo*(ambient+float3(1.0,0.92,0.80)*diffuse*sun.w);
            }
            if(emission.x>0)radiance+=emissionTex.Sample(samp,i.uv).rgb*emission.x;
            if(ambientExposure.w>0.5) {
                float d=length(camera.xyz-i.world);
                float fog=1-exp(-pow(d/max(1,camera.w),2));
                radiance=lerp(radiance,skyColor.rgb,fog);
            }
            return float4(displayColor(radiance),1);
        }
        float4 ps(O i,bool front:SV_IsFrontFace):SV_TARGET { float4 c=tex.Sample(samp,i.uv);clip(c.a-0.15);return shade(i,c.rgb,front); }
        float4 psSolid(O i,bool front:SV_IsFrontFace):SV_TARGET { return shade(i,min(i.c.rgb,float3(0.48,0.50,0.53)),front); }
        float4 waterColor(O i,bool front,bool falling) {
            float t=water.x*water.w;
            float3 baseNormal=dot(i.n,i.n)>0.0001?normalize(i.n):float3(0,1,0);
            float3 tangent=normalize(cross(abs(baseNormal.y)<0.9?float3(0,1,0):float3(0,0,1),baseNormal));
            float3 bitangent=cross(baseNormal,tangent);
            float2 p=float2(dot(i.world,tangent),dot(i.world,bitangent));
            float2 phase=float2(p.x*0.65+p.y*0.31+t*1.7,p.y*0.83-p.x*0.23-t*1.3);
            float2 ripple=float2(sin(phase.x),cos(phase.y))/(1+fwidth(phase)*fwidth(phase));
            float3 n=normalize(baseNormal+(tangent*ripple.x+bitangent*ripple.y)*water.y);
            if(!front)n=-n;
            float3 eye=normalize(camera.xyz-i.world+float3(0,0.0001,0));
            if(dot(n,eye)<0)n=-n;
            float fresnel=0.03+0.97*pow(1-saturate(dot(n,eye)),5);
            float3 reflection=skyRadiance(reflect(-eye,n));
            float3 tint=float3(0.025,0.16,0.21);
            float diffuse=ambientExposure.z>0.5?ambientExposure.x+sun.w*saturate(dot(n,sun.xyz))*0.35:1;
            float3 radiance=lerp(tint*diffuse,reflection,fresnel);
            float specular=pow(saturate(dot(reflect(-sun.xyz,n),eye)),96)*sun.w;
            if(ambientExposure.z>0.5)radiance+=float3(1,0.94,0.8)*specular;
            float foam=0;
            if(falling) {
                float streak=sin(p.x*2.4+sin(p.x*0.7)*2+ i.world.y*0.3+t*5);
                foam=smoothstep(0.05,0.95,streak)*0.65;
                radiance=lerp(radiance,float3(0.55,0.75,0.8),foam);
            }
            if(ambientExposure.w>0.5)radiance=lerp(radiance,skyColor.rgb,1-exp(-pow(length(camera.xyz-i.world)/max(1,camera.w),2)));
            return float4(displayColor(radiance),saturate(water.z+fresnel*0.2+foam*0.2));
        }
        float4 psWater(O i,bool front:SV_IsFrontFace):SV_TARGET {return waterColor(i,front,false);}
        float4 psWaterfall(O i,bool front:SV_IsFrontFace):SV_TARGET {return waterColor(i,front,true);}
        struct SkyOutput {float4 p:SV_POSITION;float2 uv:TEXCOORD;};
        SkyOutput vsSky(uint id:SV_VertexID){SkyOutput o;o.uv=float2((id<<1)&2,id&2);o.p=float4(o.uv*float2(2,-2)+float2(-1,1),1,1);return o;}
        float4 psSky(SkyOutput i):SV_TARGET {
            float2 ndc=i.uv*float2(2,-2)+float2(-1,1);
            float4 nearPoint=mul(float4(ndc,0,1),inverseViewProjection);
            float4 farPoint=mul(float4(ndc,0.9,1),inverseViewProjection);
            float3 ray=normalize(farPoint.xyz/farPoint.w-nearPoint.xyz/nearPoint.w);
            return float4(displayColor(skyRadiance(ray)),1);
        }
        """;
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDXGISwapChain swap;
    private readonly ID3D11VertexShader vs;
    private readonly ID3D11VertexShader worldVs;
    private readonly ID3D11PixelShader ps;
    private readonly ID3D11InputLayout layout;
    private readonly ID3D11InputLayout textureLayout;
    private readonly ID3D11VertexShader textureVs;
    private readonly ID3D11PixelShader texturePs;
    private readonly ID3D11PixelShader solidPs;
    private readonly ID3D11PixelShader waterPs, waterfallPs;
    private readonly System.Diagnostics.Stopwatch animationClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly ID3D11SamplerState sampler;
    private readonly ID3D11SamplerState terrainSampler;
    private readonly Dictionary<string, ID3D11ShaderResourceView> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ID3D11RasterizerState raster;
    private readonly ID3D11RasterizerState selectionRaster;
    private readonly ID3D11DepthStencilState selectionDepth;
    private readonly ID3D11PixelShader selectionPs;
    private readonly ID3D11Buffer matrixBuffer;
    private readonly ID3D11Buffer lightingBuffer;
    private readonly ID3D11Buffer materialBuffer;
    private readonly ID3D11Buffer skyBuffer;
    private readonly ID3D11VertexShader skyVs;
    private readonly ID3D11PixelShader skyPs;
    private readonly ID3D11BlendState overlayBlend;
    private readonly ID3D11RasterizerState wireRaster;
    private ID3D11Buffer? worldBuffer;
    private ID3D11Buffer? boxBuffer;
    private SceneRange[] worldRanges = [], boxRanges = [];
    private ID3D11RenderTargetView? target;
    private ID3D11DepthStencilView? depth;
    private int width, height;


    public Direct3DRenderer(IntPtr window)
    {
        var desc = new SwapChainDescription
        {
            BufferCount = 2,
            BufferDescription = new ModeDescription(0, 0, new Rational(0, 1), Format.R8G8B8A8_UNorm),
            BufferUsage = Usage.RenderTargetOutput,
            OutputWindow = window,
            SampleDescription = new SampleDescription(1, 0),
            Windowed = true,
            SwapEffect = SwapEffect.Discard
        };
        D3D11.D3D11CreateDeviceAndSwapChain(null, DriverType.Hardware, DeviceCreationFlags.None,
            [FeatureLevel.Level_11_0], desc, out var chain, out var dev, out _, out var ctx).CheckError();
        swap = chain!;
        device = dev!;
        context = ctx!;
        const string shader = "struct V { float3 p:POSITION; float4 c:COLOR; }; struct O { float4 p:SV_POSITION; float4 c:COLOR; }; O vs(V v){ O o; o.p=float4(v.p,1); o.c=v.c; return o; } float4 ps(O i):SV_TARGET{return i.c;}";
        var vertexCode = Compiler.Compile(shader, "vs", "scene", "vs_4_0");
        var pixelCode = Compiler.Compile(shader, "ps", "scene", "ps_4_0");
        vs = device.CreateVertexShader(vertexCode.Span);
        ps = device.CreatePixelShader(pixelCode.Span);
        worldVs = device.CreateVertexShader(Compiler.Compile(WorldShader, "vs", "world", "vs_4_0").Span);
        matrixBuffer = device.CreateBuffer(64, BindFlags.ConstantBuffer);
        lightingBuffer = device.CreateBuffer((uint)Marshal.SizeOf<LightingConstants>(), BindFlags.ConstantBuffer);
        materialBuffer = device.CreateBuffer(16, BindFlags.ConstantBuffer);
        skyBuffer = device.CreateBuffer((uint)Marshal.SizeOf<SkyConstants>(), BindFlags.ConstantBuffer);
        layout = device.CreateInputLayout([new("POSITION", 0, Format.R32G32B32_Float, 0, 0), new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0)], vertexCode.Span);
        var texCode = Compiler.Compile(TextureShader, "vs", "texture", "vs_4_0");
        textureVs = device.CreateVertexShader(texCode.Span);
        texturePs = device.CreatePixelShader(Compiler.Compile(TextureShader, "ps", "texture", "ps_4_0").Span);
        solidPs = device.CreatePixelShader(Compiler.Compile(TextureShader, "psSolid", "solid", "ps_4_0").Span);
        waterPs = device.CreatePixelShader(Compiler.Compile(TextureShader, "psWater", "water", "ps_4_0").Span);
        waterfallPs = device.CreatePixelShader(Compiler.Compile(TextureShader, "psWaterfall", "waterfall", "ps_4_0").Span);
        skyVs = device.CreateVertexShader(Compiler.Compile(TextureShader, "vsSky", "sky", "vs_4_0").Span);
        skyPs = device.CreatePixelShader(Compiler.Compile(TextureShader, "psSky", "sky", "ps_4_0").Span);
        overlayBlend = device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha));
        wireRaster = device.CreateRasterizerState(RasterizerDescription.Wireframe);
        textureLayout = device.CreateInputLayout(TextureElements, texCode.Span);
        sampler = device.CreateSamplerState(new SamplerDescription { Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap, MaxLOD = float.MaxValue });
        terrainSampler = device.CreateSamplerState(new SamplerDescription { Filter = Filter.Anisotropic, MaxAnisotropy = 8, AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, MaxLOD = float.MaxValue });
        raster = device.CreateRasterizerState(new RasterizerDescription { FillMode = FillMode.Solid, CullMode = CullMode.None, DepthClipEnable = true });
        selectionRaster = device.CreateRasterizerState(new RasterizerDescription { FillMode = FillMode.Wireframe, CullMode = CullMode.None, DepthClipEnable = true, DepthBias = -1, SlopeScaledDepthBias = -1 });
        selectionDepth = device.CreateDepthStencilState(new DepthStencilDescription { DepthEnable = true, DepthWriteMask = DepthWriteMask.Zero, DepthFunc = ComparisonFunction.LessEqual });
        selectionPs = device.CreatePixelShader(Compiler.Compile("float4 ps():SV_TARGET{return float4(1,0.65,0.05,1);}", "ps", "selection", "ps_4_0").Span);
    }

    public void SetWorld(Vertex[] vertices, Vertex[] boxes, SceneRange[] geometryRanges, SceneRange[] boundsRanges)
    {
        worldBuffer?.Dispose();
        boxBuffer?.Dispose();
        worldBuffer = null;
        boxBuffer = null;
        worldRanges = geometryRanges;
        boxRanges = boundsRanges;
        foreach (var texture in textures.Values)
            texture.Dispose();
        textures.Clear();
        foreach (var path in geometryRanges.SelectMany(r => new[] { r.Texture, r.EmissionTexture }).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                textures.Add(path!, CreateTextureView(device, context, BntxTexture.Load(path!)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or SharpGen.Runtime.SharpGenException) { }
        }
        if (vertices.Length > 0)
            worldBuffer = device.CreateBuffer(vertices, BindFlags.VertexBuffer, ResourceUsage.Immutable);
        if (boxes.Length > 0)
            boxBuffer = device.CreateBuffer(boxes, BindFlags.VertexBuffer, ResourceUsage.Immutable);
    }
    internal static InputElementDescription[] TextureElements => [new("POSITION", 0, Format.R32G32B32_Float, 0, 0), new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0), new("TEXCOORD", 0, Format.R32G32_Float, 28, 0), new("NORMAL", 0, Format.R32G32B32_Float, 36, 0)];
    public void Draw(int w, int h, Matrix4x4 viewProjection, Vertex[] vertices, int categoryMask, bool showBounds, Vertex[] selectedBounds, bool gameRendering = true, bool showTerrain = true, HashSet<Actor>? nameMatches = null, bool filterModels = false, Actor? selectedActor = null, RenderSettings? settings = null, Vector3 cameraPosition = default, Vertex[]? zoneFaces = null, Vertex[]? zoneLines = null, bool zoneXray = true, bool present = true, float? timeSeconds = null)
    {
        if (w <= 0 || h <= 0)
            return;
        settings ??= new RenderSettings();
        if (target == null || width != w || height != h)
        {
            context.ClearState();
            target?.Dispose();
            depth?.Dispose();
            target = null;
            depth = null;
            swap.ResizeBuffers(0, (uint)w, (uint)h, Format.Unknown, SwapChainFlags.None).CheckError();
            using var back = swap.GetBuffer<ID3D11Texture2D>(0);
            target = device.CreateRenderTargetView(back);
            using var depthTexture = device.CreateTexture2D(new Texture2DDescription { Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1, Format = Format.D32_Float, SampleDescription = new SampleDescription(1, 0), BindFlags = BindFlags.DepthStencil });
            depth = device.CreateDepthStencilView(depthTexture);
            width = w;
            height = h;
        }
        context.ClearRenderTargetView(target, new Color4(0.035f, 0.055f, 0.08f, 1));
        context.ClearDepthStencilView(depth!, DepthStencilClearFlags.Depth, 1, 0);
        context.OMSetRenderTargets(target, depth);
        context.RSSetViewport(0, 0, w, h);
        context.RSSetState(raster);
        context.OMSetBlendState(null);
        context.OMSetDepthStencilState(null);
        context.UpdateSubresource(settings.Constants(cameraPosition, settings.AnimateWater ? timeSeconds ?? (float)animationClock.Elapsed.TotalSeconds : 0), lightingBuffer);
        context.PSSetConstantBuffer(1, lightingBuffer);
        context.PSSetConstantBuffer(2, materialBuffer);
        if (!Matrix4x4.Invert(viewProjection, out var inverseViewProjection))
            inverseViewProjection = Matrix4x4.Identity;
        context.UpdateSubresource(new SkyConstants { InverseViewProjection = inverseViewProjection, Options = new(settings.Sky ? 1 : 0, settings.Clouds, 0, 0) }, skyBuffer);
        context.PSSetConstantBuffer(3, skyBuffer);
        context.OMSetRenderTargets(target);
        context.IASetInputLayout(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(skyVs);
        context.PSSetShader(skyPs);
        context.Draw(3, 0);
        context.OMSetRenderTargets(target, depth);
        context.RSSetState(settings.Wireframe ? wireRaster : raster);
        context.IASetInputLayout(layout);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.PSSetShader(ps);
        context.UpdateSubresource(viewProjection, matrixBuffer);
        context.VSSetConstantBuffer(0, matrixBuffer);
        if (worldBuffer != null)
        {
            context.UpdateSubresource(viewProjection, matrixBuffer);
            context.VSSetConstantBuffer(0, matrixBuffer);
            context.IASetVertexBuffer(0, worldBuffer, (uint)Marshal.SizeOf<Vertex>());
            context.VSSetShader(worldVs);
            foreach (var range in worldRanges)
                if ((categoryMask & range.Mask) != 0 && range.Count > 0)
                {
                    if (range.Terrain && !showTerrain)
                        continue;
                    if (settings.Water && range.Water != WaterKind.None)
                        continue;
                    if (filterModels && range.Owner != null && nameMatches != null && !nameMatches.Contains(range.Owner))
                        continue;
                    ID3D11ShaderResourceView? emissionView = null;
                    if (gameRendering && settings.Emission && range.EmissionTexture != null)
                        textures.TryGetValue(range.EmissionTexture, out emissionView);
                    // D3D11 accepts null to unbind the previous material's map.
                    context.PSSetShaderResource(1, emissionView!);
                    context.UpdateSubresource(new Vector4(emissionView == null ? 0 : settings.EmissionStrength, 0, 0, 0), materialBuffer);
                    context.PSSetSampler(0, range.Terrain ? terrainSampler : sampler);
                    if (gameRendering && range.Texture != null && textures.TryGetValue(range.Texture, out var tex))
                    {
                        context.IASetInputLayout(textureLayout);
                        context.VSSetShader(textureVs);
                        context.PSSetShader(texturePs);
                        context.PSSetShaderResource(0, tex);
                    }
                    else
                    {
                        context.IASetInputLayout(textureLayout);
                        context.VSSetShader(textureVs);
                        context.PSSetShader(solidPs);
                    }
                    context.Draw(range.Count, range.Start);
                }
            if (settings.Water)
            {
                context.IASetInputLayout(textureLayout);
                context.VSSetShader(textureVs);
                context.OMSetBlendState(overlayBlend);
                context.OMSetDepthStencilState(selectionDepth);
                foreach (var range in worldRanges.Where(r => r.Water is WaterKind.Surface or WaterKind.Waterfall)
                    .OrderByDescending(r => Vector3.DistanceSquared(cameraPosition, r.Center)))
                {
                    if ((categoryMask & range.Mask) == 0 || (filterModels && range.Owner != null && nameMatches != null && !nameMatches.Contains(range.Owner)))
                        continue;
                    context.PSSetShader(range.Water == WaterKind.Waterfall ? waterfallPs : waterPs);
                    context.Draw(range.Count, range.Start);
                }
                context.OMSetBlendState(null);
                context.OMSetDepthStencilState(null);
            }
            if (selectedActor != null && (nameMatches == null || nameMatches.Contains(selectedActor)))
            {
                context.IASetInputLayout(layout);
                context.VSSetShader(worldVs);
                context.PSSetShader(selectionPs);
                context.RSSetState(selectionRaster);
                context.OMSetDepthStencilState(selectionDepth);
                foreach (var range in worldRanges)
                    if (ReferenceEquals(range.Owner, selectedActor) && (categoryMask & range.Mask) != 0 && !(settings.Water && range.Water is WaterKind.Volume or WaterKind.Backface))
                        context.Draw(range.Count, range.Start);
                context.RSSetState(raster);
                context.OMSetDepthStencilState(null);
            }
        }
        context.RSSetState(raster);
        if (showBounds && boxBuffer != null)
        {
            context.IASetInputLayout(layout);
            context.PSSetShader(ps);
            context.IASetVertexBuffer(0, boxBuffer, (uint)Marshal.SizeOf<Vertex>());
            context.VSSetShader(worldVs);
            context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
            foreach (var range in boxRanges)
                if ((categoryMask & range.Mask) != 0 && range.Count > 0 && (range.Owner == null || nameMatches == null || nameMatches.Contains(range.Owner)))
                    context.Draw(range.Count, range.Start);
        }
        DrawOverlay(zoneFaces, PrimitiveTopology.TriangleList, zoneXray);
        DrawOverlay(zoneLines, PrimitiveTopology.LineList, zoneXray);
        if (selectedBounds.Length > 0)
        {
            using var selection = device.CreateBuffer(selectedBounds, BindFlags.VertexBuffer);
            context.OMSetRenderTargets(target);
            context.IASetVertexBuffer(0, selection, (uint)Marshal.SizeOf<Vertex>());
            context.IASetInputLayout(layout);
            context.PSSetShader(ps);
            context.VSSetShader(worldVs);
            context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
            context.Draw((uint)selectedBounds.Length, 0);
        }
        if (vertices.Length > 0)
        {
            using var buffer = device.CreateBuffer(vertices, BindFlags.VertexBuffer);
            context.OMSetRenderTargets(target);
            context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<Vertex>());
            context.IASetInputLayout(layout);
            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(vs);
            context.PSSetShader(ps);
            context.Draw((uint)vertices.Length, 0);
        }

        if (present)
            swap.Present(1, PresentFlags.None).CheckError();
    }

    private void DrawOverlay(Vertex[]? data, PrimitiveTopology topology, bool xray)
    {
        if (data is not { Length: > 0 })
            return;
        using var buffer = device.CreateBuffer(data, BindFlags.VertexBuffer);
        if (xray)
            context.OMSetRenderTargets(target!);
        else
            context.OMSetRenderTargets(target!, depth);
        context.OMSetBlendState(overlayBlend);
        context.OMSetDepthStencilState(selectionDepth);
        context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<Vertex>());
        context.IASetInputLayout(layout);
        context.IASetPrimitiveTopology(topology);
        context.VSSetShader(worldVs);
        context.PSSetShader(ps);
        context.Draw((uint)data.Length, 0);
        context.OMSetBlendState(null);
        context.OMSetDepthStencilState(null);
    }

    internal static ID3D11ShaderResourceView CreateTextureView(ID3D11Device device, ID3D11DeviceContext context, BntxTexture texture)
    {
        bool mips = texture.BlockBytes == 4;
        var desc = new Texture2DDescription { Width = (uint)texture.PaddedWidth, Height = (uint)texture.PaddedHeight, MipLevels = mips ? 0u : 1u, ArraySize = 1, Format = texture.Format, SampleDescription = new SampleDescription(1, 0), BindFlags = BindFlags.ShaderResource | (mips ? BindFlags.RenderTarget : 0), MiscFlags = mips ? ResourceOptionFlags.GenerateMips : 0 };
        using var image = device.CreateTexture2D(desc);
        context.UpdateSubresource(texture.Data, image, 0, (uint)texture.RowPitch, 0);
        var view = device.CreateShaderResourceView(image);
        if (mips)
            context.GenerateMips(view);
        return view;
    }
    public void Dispose()
    {
        foreach (var tex in textures.Values)
            tex.Dispose();
        selectionPs.Dispose();
        selectionRaster.Dispose();
        selectionDepth.Dispose();
        terrainSampler.Dispose();
        sampler.Dispose();
        textureLayout.Dispose();
        solidPs.Dispose();
        waterPs.Dispose();
        waterfallPs.Dispose();
        texturePs.Dispose();
        textureVs.Dispose();
        boxBuffer?.Dispose();
        worldBuffer?.Dispose();
        matrixBuffer.Dispose();
        lightingBuffer.Dispose();
        materialBuffer.Dispose();
        skyBuffer.Dispose();
        skyVs.Dispose();
        skyPs.Dispose();
        overlayBlend.Dispose();
        wireRaster.Dispose();
        depth?.Dispose();
        target?.Dispose();
        raster.Dispose();
        layout.Dispose();
        ps.Dispose();
        worldVs.Dispose();
        vs.Dispose();
        swap.Dispose();
        context.Dispose();
        device.Dispose();
    }
}

readonly record struct SceneRange(uint Start, uint Count, int Mask, string? Texture = null, bool Terrain = false, Actor? Owner = null, WaterKind Water = WaterKind.None, Vector3 Center = default, string? EmissionTexture = null);
