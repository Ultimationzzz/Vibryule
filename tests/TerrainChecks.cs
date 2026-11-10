using System.Text;
namespace OotSceneViewer;

static class TerrainChecks
{
    internal static string SignedHeightChecks()
    {
        byte[] Archive(float height)
        {
            const int start = 0x40, size = 0x66000;
            var data = new byte[start + size];
            void Int(int p, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(p), value);
            Int(0, 0x01020401);
            Int(4, 11);
            Int(12, 257);
            Int(24, 257);
            Int(0x2c, start - 0x2c);
            Int(0x30, size);
            for (int p = start; p < data.Length; p += 4)
                Int(p, BitConverter.SingleToInt32Bits(height));
            using var compressor = new ZstdSharp.Compressor();
            byte[] compressed = compressor.Wrap(data).ToArray();
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write("TARB"u8);
            writer.Write(1);
            writer.Write((ushort)0);
            writer.Write((ushort)20);
            writer.Write(data.Length);
            writer.Write(compressed.Length);
            writer.Write(compressed);
            return output.ToArray();
        }
        foreach (float height in new[] { -0.125f, 0f, 1.25f })
        {
            var decoded = TerrainResource.ReadHeights(Archive(height));
            if (decoded.Length != 257 * 257 || decoded.Any(h => h != height))
                throw new InvalidOperationException("Float terrain heights were clamped or changed.");
        }
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            try
            {
                TerrainResource.ReadHeights(Archive(bad));
                throw new InvalidOperationException("Non-finite terrain heights were accepted.");
            }
            catch (InvalidDataException) { }
        }
        return "PASS: signed/unbounded float terrain heights are preserved; NaN and infinities are rejected.";
    }

    internal static string Run(string nupPath, string name)
    {
        using var nup = new NupReader(nupPath);
        CancellationToken cancel = default;
        if (!nup.Files.TryGetValue($"/TerrainArc/{name}.tscb", out var descriptorEntry))
            throw new ArgumentException($"Unknown terrain '{name}'. Available: {string.Join(", ", nup.Files.Keys.Where(k => k.StartsWith("/TerrainArc/") && k.EndsWith(".tscb")))}");
        var descriptor = TerrainResource.ReadDescriptor(nup.ReadMember(descriptorEntry, cancel));
        string prefix = $"/TerrainArc/{name}/";
        var tiles = TerrainResource.SelectTiles(descriptor, t => nup.Files.ContainsKey(prefix + t + ".hght"));
        if (tiles.Count == 0)
            throw new InvalidDataException("No terrain tiles selected.");
        var maps = tiles.ToDictionary(t => t.Name, t => TerrainResource.ReadHeights(nup.ReadMember(nup.Files[prefix + t.Name + ".hght"], cancel)));
        var regions = tiles.SelectMany(t => Enumerable.Range(0, 4).Where(q => (t.Quadrants & (1 << q)) != 0).Select(q => (X: t.X + ((q & 1) == 0 ? -1 : 1) * t.Size / 4, Z: t.Z + ((q & 2) == 0 ? -1 : 1) * t.Size / 4, Size: t.Size / 2))).ToArray();
        for (int i = 0; i < regions.Length; i++)
            for (int j = i + 1; j < regions.Length; j++)
            {
                var a = regions[i];
                var b = regions[j];
                if ((a.Size + b.Size) / 2 - Math.Abs(a.X - b.X) > 0.001f && (a.Size + b.Size) / 2 - Math.Abs(a.Z - b.Z) > 0.001f)
                    throw new InvalidDataException("Selected terrain LOD regions overlap.");
            }
        double maxSeam = 0;
        int edges = 0, badWeights = 0, weightPixels = 0;
        float min = float.MaxValue, max = float.MinValue;
        var unknown = new Dictionary<byte, int>();
        foreach (var tile in tiles)
        {
            var heights = maps[tile.Name];
            min = Math.Min(min, heights.Min() * descriptor.HeightScale);
            max = Math.Max(max, heights.Max() * descriptor.HeightScale);
            foreach (var other in tiles)
            {
                if (ReferenceEquals(tile, other))
                    continue;
                if (Math.Abs(tile.Size - other.Size) > 0.001f)
                    continue;
                bool east = Math.Abs(other.X - tile.X - tile.Size) < 0.001f && Math.Abs(other.Z - tile.Z) < 0.001f;
                bool south = Math.Abs(other.Z - tile.Z - tile.Size) < 0.001f && Math.Abs(other.X - tile.X) < 0.001f;
                if (!east && !south)
                    continue;
                edges++;
                var next = maps[other.Name];
                for (int i = 0; i < 257; i++)
                    maxSeam = Math.Max(maxSeam, Math.Abs(east ? heights[i * 257 + 256] - next[i * 257] : heights[256 * 257 + i] - next[i]) * descriptor.HeightScale);
            }
            if (nup.Files.TryGetValue(prefix + tile.Name + ".mate", out var material))
            {
                var m = TerrainResource.ReadMaterials(nup.ReadMember(material, cancel));
                for (int i = 0; i < m.Indices.Length; i += 4)
                {
                    weightPixels++;
                    int sum = 0;
                    for (int c = 0; c < 4; c++)
                    {
                        if (m.Indices[i + c] < descriptor.Materials.Count)
                            sum += m.Weights[i + c];
                        else if (m.Weights[i + c] != 0)
                        {
                            unknown.TryGetValue(m.Indices[i + c], out var n);
                            unknown[m.Indices[i + c]] = n + 1;
                        }
                    }
                    if (sum != 255 && sum != 0)
                        badWeights++;
                }
            }
        }
        var report = new StringBuilder($"{name}: {tiles.Count} tiles covering {regions.Length} non-overlapping quadrants, height range {min:F3}..{max:F3}, {edges} adjacent edges, maximum edge height error {maxSeam:F6}.\nMaterial maps: {weightPixels} pixels, {badWeights} weights needing normalization. Unknown IDs: {string.Join(", ", unknown)}\n");
        if (name == "HyruleField_02")
        {
            foreach (var p in new[] { (-39.58769f, 200.61032f, 150.56625f), (0.007324921f, 53.62816f, 150.467f), (-53.233795f, 208.5894f, 150.64435f) })
            {
                var tile = tiles.Where(t => Math.Abs(p.Item1 - t.X) <= t.Size / 2 && Math.Abs(p.Item2 - t.Z) <= t.Size / 2).OrderBy(t => t.Size).First();
                var h = maps[tile.Name];
                float x = ((p.Item1 - tile.X) / tile.Size + 0.5f) * 256, z = ((p.Item2 - tile.Z) / tile.Size + 0.5f) * 256;
                int ix = Math.Clamp((int)x, 0, 255), iz = Math.Clamp((int)z, 0, 255);
                float fx = x - ix, fz = z - iz;
                float value = ((h[iz * 257 + ix] * (1 - fx) + h[iz * 257 + ix + 1] * fx) * (1 - fz) + (h[(iz + 1) * 257 + ix] * (1 - fx) + h[(iz + 1) * 257 + ix + 1] * fx) * fz) * descriptor.HeightScale;
                report.AppendLine($"Placement ({p.Item1},{p.Item2}): actor Y {p.Item3:F3}, terrain Y {value:F3}, delta {p.Item3 - value:F3}.");
            }
        }
        if (maxSeam > 0.025)
            throw new InvalidDataException("Terrain boundaries do not match.\n" + report);
        if (unknown.Count > 0)
            throw new InvalidDataException("Unknown terrain materials.\n" + report);
        // A corrupt input must fail before a geometry buffer can be assembled.
        try
        {
            TerrainResource.ReadHeights(new byte[20]);
            throw new InvalidOperationException("Malformed terrain was accepted.");
        }
        catch (InvalidDataException) { }
        return "PASS: " + report;
    }
}
