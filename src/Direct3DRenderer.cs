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

sealed class Direct3DRenderer : IDisposable
{
    internal const string WorldShader = "cbuffer Camera : register(b0) { row_major float4x4 viewProjection; }; struct V { float3 p:POSITION; float4 c:COLOR; }; struct O { float4 p:SV_POSITION; float4 c:COLOR; }; O vs(V v){ O o; o.p=mul(float4(v.p,1),viewProjection); o.c=v.c; return o; }";
    internal const string TextureShader = """
        cbuffer Camera : register(b0) { row_major float4x4 viewProjection; };
        Texture2D tex:register(t0); SamplerState samp:register(s0);
        struct V { float3 p:POSITION; float4 c:COLOR; float2 uv:TEXCOORD; float3 n:NORMAL; };
        struct O { float4 p:SV_POSITION; float4 c:COLOR; float2 uv:TEXCOORD; float3 n:NORMAL; };
        O vs(V v) { O o; o.p=mul(float4(v.p,1),viewProjection); o.c=v.c; o.uv=v.uv; o.n=v.n; return o; }
        float4 ps(O i, bool front:SV_IsFrontFace):SV_TARGET {
            float4 c=tex.Sample(samp,i.uv); clip(c.a-0.15);
            float3 lighting=i.c.rgb;
            if(dot(i.n,i.n)>0.01) {
                float3 n=normalize(i.n);
                // Two-sided foliage remains readable. Terrain normals point up.
                float sun=abs(dot(n,normalize(float3(0.3,0.8,0.4))));
                float sky=saturate(n.y*0.5+0.5);
                lighting*=lerp(float3(0.22,0.23,0.24),float3(0.35,0.40,0.46),sky)+float3(0.65,0.60,0.50)*sun;
            }
            return float4(pow(saturate(c.rgb*lighting),1.0/2.2),1);
        }
        float4 psSolid(O i):SV_TARGET {
            float3 color=i.c.rgb;
            if(dot(i.n,i.n)>0.01) {
                float3 n=normalize(i.n);
                color=float3(0.34,0.38,0.40)*(0.32+0.68*abs(dot(n,normalize(float3(0.3,0.8,0.4)))));
                color=pow(color,1.0/2.2);
            }
            return float4(color,1);
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
    private readonly ID3D11SamplerState sampler;
    private readonly ID3D11SamplerState terrainSampler;
    private readonly Dictionary<string, ID3D11ShaderResourceView> textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ID3D11RasterizerState raster;
    private readonly ID3D11RasterizerState selectionRaster;
    private readonly ID3D11DepthStencilState selectionDepth;
    private readonly ID3D11PixelShader selectionPs;
    private readonly ID3D11Buffer matrixBuffer;
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
        layout = device.CreateInputLayout([new("POSITION", 0, Format.R32G32B32_Float, 0, 0), new("COLOR", 0, Format.R32G32B32A32_Float, 12, 0)], vertexCode.Span);
        var texCode = Compiler.Compile(TextureShader, "vs", "texture", "vs_4_0");
        textureVs = device.CreateVertexShader(texCode.Span);
        texturePs = device.CreatePixelShader(Compiler.Compile(TextureShader, "ps", "texture", "ps_4_0").Span);
        solidPs = device.CreatePixelShader(Compiler.Compile(TextureShader, "psSolid", "solid", "ps_4_0").Span);
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
        foreach (var path in geometryRanges.Select(r => r.Texture).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase))
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
    public void Draw(int w, int h, Matrix4x4 viewProjection, Vertex[] vertices, int categoryMask, bool showBounds, Vertex[] selectedBounds, bool gameRendering = true, bool showTerrain = true, HashSet<Actor>? nameMatches = null, bool filterModels = false, Actor? selectedActor = null)
    {
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
                    if (filterModels && range.Owner != null && nameMatches != null && !nameMatches.Contains(range.Owner))
                        continue;
                    if (gameRendering && range.Texture != null && textures.TryGetValue(range.Texture, out var tex))
                    {
                        context.IASetInputLayout(textureLayout);
                        context.VSSetShader(textureVs);
                        context.PSSetShader(texturePs);
                        context.PSSetShaderResource(0, tex);
                        context.PSSetSampler(0, range.Terrain ? terrainSampler : sampler);
                    }
                    else
                    {
                        context.IASetInputLayout(textureLayout);
                        context.VSSetShader(textureVs);
                        context.PSSetShader(solidPs);
                    }
                    context.Draw(range.Count, range.Start);
                }
            if (selectedActor != null && (nameMatches == null || nameMatches.Contains(selectedActor)))
            {
                context.IASetInputLayout(layout);
                context.VSSetShader(worldVs);
                context.PSSetShader(selectionPs);
                context.RSSetState(selectionRaster);
                context.OMSetDepthStencilState(selectionDepth);
                foreach (var range in worldRanges)
                    if (ReferenceEquals(range.Owner, selectedActor) && (categoryMask & range.Mask) != 0)
                        context.Draw(range.Count, range.Start);
                context.RSSetState(raster);
                context.OMSetDepthStencilState(null);
            }
        }
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

        swap.Present(1, PresentFlags.None).CheckError();
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
        texturePs.Dispose();
        textureVs.Dispose();
        boxBuffer?.Dispose();
        worldBuffer?.Dispose();
        matrixBuffer.Dispose();
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

readonly record struct SceneRange(uint Start, uint Count, int Mask, string? Texture = null, bool Terrain = false, Actor? Owner = null);
