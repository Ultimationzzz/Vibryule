using System.Numerics;
using System.Text.Json;
using Vortice.DXGI;

namespace OotSceneViewer;

static class TerrainScene
{
    private const int Grid = 65, TextureSize = 512;
    internal static string Prepare(NupReader nup, SceneOption scene, string directory, Func<NupReader.Entry, byte[]> expand, IProgress<string> progress, CancellationToken cancel)
    {
        if (!nup.Files.TryGetValue($"/Banc/GameBancParam/{scene.Name}.game__banc__GameBancParam.bgyml", out var banc))
            return "No scene terrain reference.";
        var definition = new BymlReader(nup.ReadMember(banc, cancel)).Root;
        string? name = definition?["TerrainSceneName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
            return "Scene has no terrain.";
        if (name.IndexOfAny(['/', '\\']) >= 0 || !nup.Files.TryGetValue($"/TerrainArc/{name}.tscb", out var tscb))
            throw new InvalidDataException("Scene terrain descriptor is missing.");
        var descriptor = TerrainResource.ReadDescriptor(nup.ReadMember(tscb, cancel));
        string prefix = $"/TerrainArc/{name}/";
        var tiles = TerrainResource.SelectTiles(descriptor, t => nup.Files.ContainsKey(prefix + t + ".hght"));
        if (tiles.Count == 0 || tiles.Count > 2048)
            throw new InvalidDataException("Unsupported terrain tile count.");
        progress.Report($"Reading {name}: {tiles.Count} terrain tiles…");
        byte[]? albedo = null;
        var layers = new Dictionary<(int Layer, int Size), BntxTexture>();
        BntxTexture Layer(int index, int maxSize)
        {
            if (layers.TryGetValue((index, maxSize), out var texture))
                return texture;
            if (albedo == null)
            {
                if (!nup.Files.TryGetValue("/Tex/MaterialAlb.bntx.zs", out var entry))
                    throw new InvalidDataException("Terrain material texture array is missing.");
                albedo = expand(entry);
            }
            texture = BntxTexture.Decode(albedo, index, maxSize);
            if (texture.BlockBytes != 4)
                throw new InvalidDataException("Terrain material array is not decoded RGBA.");
            layers.Add((index, maxSize), texture);
            return texture;
        }
        var records = new List<(TerrainResource.Tile Tile, float[] Heights, Vector3[] Normals, string Texture)>();
        int ordinal = 0;
        foreach (var tile in tiles)
        {
            cancel.ThrowIfCancellationRequested();
            var heights = TerrainResource.ReadHeights(nup.ReadMember(nup.Files[prefix + tile.Name + ".hght"], cancel));
            if (heights.All(h => h == 0))
                continue;
            var scaled = new float[Grid * Grid];
            var normals = new Vector3[Grid * Grid];
            float step = tile.Size / 256;
            for (int z = 0; z < Grid; z++)
                for (int x = 0; x < Grid; x++)
                {
                    int hx = x * 4, hz = z * 4, loX = Math.Max(hx - 1, 0), hiX = Math.Min(hx + 1, 256), loZ = Math.Max(hz - 1, 0), hiZ = Math.Min(hz + 1, 256);
                    float dx = (heights[hz * 257 + hiX] - heights[hz * 257 + loX]) * descriptor.HeightScale / ((hiX - loX) * step);
                    float dz = (heights[hiZ * 257 + hx] - heights[loZ * 257 + hx]) * descriptor.HeightScale / ((hiZ - loZ) * step);
                    scaled[z * Grid + x] = heights[hz * 257 + hx] * descriptor.HeightScale;
                    normals[z * Grid + x] = Vector3.Normalize(new(-dx, 1, -dz));
                }
            string textureName = "";
            string variant = scene.Name.Contains("Adult", StringComparison.Ordinal) ? ".adult" : "";
            if (!nup.Files.TryGetValue(prefix + tile.Name + variant + ".mate", out var materialEntry))
                nup.Files.TryGetValue(prefix + tile.Name + ".mate", out materialEntry);
            if (materialEntry != null)
            {
                var maps = TerrainResource.ReadMaterials(nup.ReadMember(materialEntry, cancel));
                var rgba = Bake(tile, maps.Indices, maps.Weights, descriptor.Materials, Layer, cancel);
                textureName = $"terrain{ordinal:D4}.tex";
                new BntxTexture(TextureSize, TextureSize, Format.R8G8B8A8_UNorm_SRgb, 4, rgba).Save(Path.Combine(directory, textureName));
            }
            records.Add((tile, scaled, normals, textureName));
            ordinal++;
            progress.Report($"Preparing terrain: {ordinal}/{tiles.Count} tiles");
        }
        // Snap only fine tile boundary vertices to the adjacent coarser mesh.
        // This removes T-junction cracks without adding overlapping skirts.
        bool Contains(TerrainResource.Tile tile, float x, float z)
        {
            if (Math.Abs(x - tile.X) > tile.Size / 2 || Math.Abs(z - tile.Z) > tile.Size / 2)
                return false;
            return (tile.Quadrants & (1 << ((x >= tile.X ? 1 : 0) + (z >= tile.Z ? 2 : 0)))) != 0;
        }
        foreach (var record in records.OrderByDescending(r => r.Tile.Size))
        {
            for (int edge = 0; edge < 4; edge++)
                for (int i = 0; i < Grid; i++)
                {
                    int x = edge == 0 ? 0 : edge == 1 ? Grid - 1 : i, z = edge == 2 ? 0 : edge == 3 ? Grid - 1 : i;
                    float wx = record.Tile.X + (x / (float)(Grid - 1) - 0.5f) * record.Tile.Size, wz = record.Tile.Z + (z / (float)(Grid - 1) - 0.5f) * record.Tile.Size;
                    float sx = wx + (edge == 0 ? -0.001f : edge == 1 ? 0.001f : 0), sz = wz + (edge == 2 ? -0.001f : edge == 3 ? 0.001f : 0);
                    var neighbor = records.Where(r => r.Tile.Size > record.Tile.Size && Contains(r.Tile, sx, sz)).OrderBy(r => r.Tile.Size).FirstOrDefault();
                    if (neighbor.Heights == null)
                        continue;
                    float fx = Math.Clamp(((wx - neighbor.Tile.X) / neighbor.Tile.Size + 0.5f) * (Grid - 1), 0, Grid - 1), fz = Math.Clamp(((wz - neighbor.Tile.Z) / neighbor.Tile.Size + 0.5f) * (Grid - 1), 0, Grid - 1);
                    int ix = Math.Min((int)fx, Grid - 2), iz = Math.Min((int)fz, Grid - 2);
                    fx -= ix;
                    fz -= iz;
                    var h = neighbor.Heights;
                    record.Heights[z * Grid + x] = (h[iz * Grid + ix] * (1 - fx) + h[iz * Grid + ix + 1] * fx) * (1 - fz) + (h[(iz + 1) * Grid + ix] * (1 - fx) + h[(iz + 1) * Grid + ix + 1] * fx) * fz;
                }
        }
        using (var file = new FileStream(Path.Combine(directory, "terrain.bin"), FileMode.CreateNew))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(0x32525456);
            writer.Write(records.Count);
            foreach (var record in records)
            {
                writer.Write(record.Tile.Name);
                writer.Write(record.Tile.X);
                writer.Write(record.Tile.Z);
                writer.Write(record.Tile.Size);
                writer.Write(record.Tile.Quadrants);
                writer.Write(Grid);
                writer.Write(record.Texture);
                for (int i = 0; i < record.Heights.Length; i++)
                {
                    writer.Write(record.Heights[i]);
                    var n = record.Normals[i];
                    writer.Write(n.X);
                    writer.Write(n.Y);
                    writer.Write(n.Z);
                }
            }
        }
        float[]? Focus(string key)
        {
            var p = definition?["HeightMapParam"]?[key];
            return p == null ? null : new[] { p["X"]!.GetValue<float>(), p["Y"]!.GetValue<float>(), p["Z"]!.GetValue<float>() };
        }
        var report = new
        {
            scene = name,
            tiles = records.Count,
            sourceTiles = records.Select(r => r.Tile.Name),
            materialLayers = layers.Keys.Select(k => k.Layer).Distinct().Order().ToArray(),
            grid = Grid,
            textureSize = TextureSize,
            focusMin = Focus("BoundingBoxMin"),
            focusMax = Focus("BoundingBoxMax"),
            notes = "Decoded TSCB/TARB heights and material weights; 64 subdivisions per tile. Reconstructed lighting, not original game shaders."
        };
        File.WriteAllText(Path.Combine(directory, "terrain-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return $"{name}: {records.Count} terrain tiles, {layers.Keys.Select(k => k.Layer).Distinct().Count()} material layers.";
    }

    internal static (Vector3 Min, Vector3 Max)? FocusBounds(string? directory)
    {
        if (directory == null || !File.Exists(Path.Combine(directory, "terrain-report.json")))
            return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "terrain-report.json")));
        if (!doc.RootElement.TryGetProperty("focusMin", out var min) || min.ValueKind != JsonValueKind.Array || !doc.RootElement.TryGetProperty("focusMax", out var max) || max.ValueKind != JsonValueKind.Array || min.GetArrayLength() != 3 || max.GetArrayLength() != 3)
            return null;
        Vector3 a = new(min[0].GetSingle(), min[1].GetSingle(), min[2].GetSingle()), b = new(max[0].GetSingle(), max[1].GetSingle(), max[2].GetSingle());
        return float.IsFinite(a.LengthSquared()) && float.IsFinite(b.LengthSquared()) && b.X > a.X && b.Y >= a.Y && b.Z > a.Z ? (a, b) : null;
    }

    private static byte[] Bake(TerrainResource.Tile tile, byte[] indices, byte[] weights, List<TerrainResource.Material> materials, Func<int, int, BntxTexture> layer, CancellationToken cancel)
    {
        var output = new byte[TextureSize * TextureSize * 4];
        var sampledLayers = new Dictionary<int, BntxTexture>();
        foreach (int id in indices.Distinct().Where(id => id < materials.Count))
        {
            var m = materials[id];
            float repetitions = tile.Size * Math.Max(Math.Abs(m.Scale.X), Math.Abs(m.Scale.Y));
            // Use the game's prefiltered mip chain at the bake's texel footprint.
            int desired = Math.Clamp((int)MathF.Ceiling(TextureSize / Math.Max(repetitions, 0.01f)), 1, 512), size = 1;
            while (size < desired)
                size *= 2;
            sampledLayers[id] = layer(m.Layer, size);
        }
        // Blend the original albedos in linear light, then cache sRGB for the GPU.
        var linear = Enumerable.Range(0, 256).Select(v => MathF.Pow(v / 255f, 2.2f)).ToArray();
        Vector3 Sample(BntxTexture texture, float u, float v)
        {
            float px = (u - MathF.Floor(u)) * texture.Width - 0.5f, py = (v - MathF.Floor(v)) * texture.Height - 0.5f;
            int ix = (int)MathF.Floor(px), iy = (int)MathF.Floor(py);
            float fx = px - ix, fy = py - iy;
            Vector3 Pixel(int x, int y)
            {
                int p = (((y % texture.Height + texture.Height) % texture.Height) * texture.Width + (x % texture.Width + texture.Width) % texture.Width) * 4;
                return new(linear[texture.Data[p]], linear[texture.Data[p + 1]], linear[texture.Data[p + 2]]);
            }
            return Vector3.Lerp(Vector3.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), fx), Vector3.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), fx), fy);
        }
        for (int y = 0; y < TextureSize; y++)
        {
            cancel.ThrowIfCancellationRequested();
            for (int x = 0; x < TextureSize; x++)
            {
                float u = (x + 0.5f) / TextureSize, v = (y + 0.5f) / TextureSize, wx = tile.X + (u - 0.5f) * tile.Size, wz = tile.Z + (v - 0.5f) * tile.Size;
                int mx = Math.Min((int)MathF.Round(u * 256), 256), my = Math.Min((int)MathF.Round(v * 256), 256), p = (my * 257 + mx) * 4;
                Vector3 color = Vector3.Zero;
                float sum = 0;
                for (int channel = 0; channel < 4; channel++)
                {
                    int id = indices[p + channel];
                    float weight = weights[p + channel] / 255f;
                    if (weight == 0 || id >= materials.Count)
                        continue;
                    var material = materials[id];
                    color += Sample(sampledLayers[id], wx * material.Scale.X, wz * material.Scale.Y) * weight;
                    sum += weight;
                }
                color = sum > 0 ? color / sum : new Vector3(0.08f, 0.09f, 0.04f);
                int o = (y * TextureSize + x) * 4;
                output[o] = (byte)Math.Clamp((int)(MathF.Pow(color.X, 1 / 2.2f) * 255 + 0.5f), 0, 255);
                output[o + 1] = (byte)Math.Clamp((int)(MathF.Pow(color.Y, 1 / 2.2f) * 255 + 0.5f), 0, 255);
                output[o + 2] = (byte)Math.Clamp((int)(MathF.Pow(color.Z, 1 / 2.2f) * 255 + 0.5f), 0, 255);
                output[o + 3] = 255;
            }
        }
        return output;
    }

    internal static (Vertex[] Vertices, SceneRange[] Ranges, int Tiles) Load(string? directory)
    {
        if (directory == null || !File.Exists(Path.Combine(directory, "terrain.bin")))
            return ([], [], 0);
        using var file = File.OpenRead(Path.Combine(directory, "terrain.bin"));
        using var reader = new BinaryReader(file);
        if (reader.ReadInt32() != 0x32525456)
            throw new InvalidDataException("Unsupported cached terrain. Prepare the scene again from its NUP.");
        int count = reader.ReadInt32();
        if (count < 0 || count > 2048)
            throw new InvalidDataException("Invalid terrain tile count.");
        var vertices = new List<Vertex>();
        var ranges = new List<SceneRange>();
        for (int t = 0; t < count; t++)
        {
            _ = reader.ReadString();
            float cx = reader.ReadSingle(), cz = reader.ReadSingle(), size = reader.ReadSingle();
            int quadrants = reader.ReadInt32(), grid = reader.ReadInt32();
            var texture = reader.ReadString();
            if (!float.IsFinite(cx) || !float.IsFinite(cz) || !float.IsFinite(size) || size <= 0 || grid != Grid || quadrants < 1 || quadrants > 15)
                throw new InvalidDataException("Invalid cached terrain grid.");
            if (texture != "" && (Path.GetFileName(texture) != texture || !texture.EndsWith(".tex", StringComparison.Ordinal)))
                throw new InvalidDataException("Invalid terrain texture filename.");
            var points = new Vertex[grid * grid];
            for (int z = 0; z < grid; z++)
                for (int x = 0; x < grid; x++)
                {
                    float h = reader.ReadSingle(), nx = reader.ReadSingle(), ny = reader.ReadSingle(), nz = reader.ReadSingle();
                    if (!float.IsFinite(h) || !float.IsFinite(nx) || !float.IsFinite(ny) || !float.IsFinite(nz))
                        throw new InvalidDataException("Non-finite cached terrain vertex.");
                    float u = x / (float)(grid - 1), v = z / (float)(grid - 1);
                    points[z * grid + x] = new(cx + (u - 0.5f) * size, h, cz + (v - 0.5f) * size, 0.5f, 0.6f, 0.3f, 1, u, v, nx, ny, nz);
                    if (texture != "")
                    {
                        points[z * grid + x].R = 1;
                        points[z * grid + x].G = 1;
                        points[z * grid + x].B = 1;
                    }
                }
            int start = vertices.Count;
            for (int z = 0; z < grid - 1; z++)
                for (int x = 0; x < grid - 1; x++)
                {
                    if ((quadrants & (1 << ((x >= (grid - 1) / 2 ? 1 : 0) + (z >= (grid - 1) / 2 ? 2 : 0)))) == 0)
                        continue;
                    int a = z * grid + x, b = a + 1, c = a + grid, d = c + 1;
                    // Zero-height samples are outside the defined terrain surface.
                    if (points[a].Y == 0 && points[b].Y == 0 && points[c].Y == 0 && points[d].Y == 0)
                        continue;
                    vertices.Add(points[a]);
                    vertices.Add(points[c]);
                    vertices.Add(points[b]);
                    vertices.Add(points[b]);
                    vertices.Add(points[c]);
                    vertices.Add(points[d]);
                }
            ranges.Add(new((uint)start, (uint)(vertices.Count - start), 1, texture == "" ? null : Path.Combine(directory, texture), true));
        }
        if (file.Position != file.Length)
            throw new InvalidDataException("Unexpected cached terrain data.");
        return (vertices.ToArray(), ranges.ToArray(), count);
    }
}
