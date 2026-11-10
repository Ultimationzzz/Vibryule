using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


using ZstdSharp;

namespace OotSceneViewer;

static class ScenePreparation
{
    private const int MaxExpanded = 512 * 1024 * 1024;
    internal static List<SceneOption> List(string path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var nup = new NupReader(path);
        var scenes = nup.Scenes();
        nup.VerifyUnchanged();
        return scenes;
    }
    internal static string Prepare(string path, SceneOption option, string cache, IProgress<string> progress, CancellationToken cancellation)
    {
        using var nup = new NupReader(path);
        cancellation.ThrowIfCancellationRequested();
        var actual = nup.Scenes().SingleOrDefault(s => s.Folder == option.Folder && s.Name == option.Name) ?? throw new InvalidDataException("Scene no longer exists in this NUP.");
        Directory.CreateDirectory(cache);
        string cacheRoot = Path.GetFullPath(cache);
        string nupDir = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(nup.SourcePath)!) + Path.DirectorySeparatorChar;
        if ((Path.TrimEndingDirectorySeparator(cacheRoot) + Path.DirectorySeparatorChar).StartsWith(nupDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a cache outside the NUP directory.");
        string sceneId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actual.Folder + "\0" + actual.Name)))[..12].ToLowerInvariant();
        string parent = Path.Combine(cacheRoot, nup.Identity());
        string final = Path.Combine(parent, "t10-" + sceneId);
        string manifest = Path.Combine(final, "scene.json");
        Directory.CreateDirectory(parent);
        if (File.Exists(manifest))
        {
            nup.VerifyUnchanged();
            progress.Report("Using cached scene.");
            return manifest;
        }
        if (Directory.Exists(final))
            throw new IOException("An incomplete scene exists at the destination. Choose another cache or remove it manually.");
        string stage = Path.Combine(parent, "p-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(stage);
        string geometry = Path.Combine(stage, "g");
        Directory.CreateDirectory(geometry);
        progress.Report("Reading compression dictionaries…");
        var dictionaryEntries = nup.PackageDictionaries().ToArray();
        if (dictionaryEntries.Length == 0 || !nup.Files.TryGetValue("/Pack/ZsDic.pack.zs", out var fallbackEntry))
            throw new InvalidDataException("The NUP has no supported compression dictionaries.");
        var dictionaries = new Dictionary<int, Dictionary<string, byte[]>>();
        foreach (var entry in dictionaryEntries)
            dictionaries[entry.PackageIndex] = ReadSarc(Expand(nup.ReadMember(entry, cancellation), null, cancellation));
        byte[] DictionaryFor(NupReader.Entry entry, string name)
        {
            var package = dictionaries.ContainsKey(entry.PackageIndex) ? entry.PackageIndex : fallbackEntry.PackageIndex;
            if (!dictionaries.TryGetValue(package, out var map) || !map.TryGetValue(name, out var dictionary))
                throw new InvalidDataException($"Missing compression dictionary: {name}");
            return dictionary;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var placementFiles = new List<string>();
        foreach (var kind in actual.Kinds.Order(StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            string key = $"/Banc/{actual.Folder}/{actual.Name}_{kind}.bcett.byml.zs";
            if (!nup.Files.TryGetValue(key, out var entry))
                throw new InvalidDataException($"Missing scene placement file: {kind}");
            byte[] raw = Expand(nup.ReadMember(entry, cancellation), DictionaryFor(entry, "bcett.byml.zsdic"), cancellation);
            var root = new BymlReader(raw).Root;
            if (root is not JsonObject obj || obj["Actors"] is not JsonArray actors)
                throw new InvalidDataException("Scene placement data has no actor list.");
            foreach (var actor in actors.OfType<JsonObject>())
                if (actor["Gyaml"] is JsonValue v && v.TryGetValue<string>(out var name) && actor["Translate"] is JsonArray)
                    names.Add(name);
            string output = kind.ToLowerInvariant() + ".json";
            File.WriteAllText(Path.Combine(stage, output), root.ToJsonString());
            placementFiles.Add(output);
            progress.Report($"{kind}: {actors.Count:N0} placement records");
        }
        var candidates = new Dictionary<string, List<NupReader.Entry>>(StringComparer.Ordinal);
        foreach (var entry in nup.Files.Values)
        {
            if (!entry.Path.StartsWith("/Model/", StringComparison.Ordinal) || !entry.Path.EndsWith(".bfres.zs", StringComparison.Ordinal))
                continue;
            string filename = entry.Path[(entry.Path.LastIndexOf('/') + 1)..^9];
            string actor = filename[(filename.LastIndexOf('.') + 1)..];
            if (!names.Contains(actor))
                continue;
            if (!candidates.TryGetValue(actor, out var list))
                candidates[actor] = list = [];
            list.Add(entry);
        }
        var report = new List<object>();
        var actorMetadata = new JsonObject();
        int actorDone = 0;
        var actorTypes = names.Order(StringComparer.Ordinal).ToArray();
        foreach (var name in actorTypes)
        {
            cancellation.ThrowIfCancellationRequested();
            actorDone++;
            if (name.IndexOfAny(['/', '\\']) >= 0 || !nup.Files.TryGetValue("/Pack/Actor/" + name + ".pack.zs", out var entry))
                continue;
            try
            {
                var packed = Expand(nup.ReadMember(entry, cancellation), DictionaryFor(entry, "pack.zsdic"), cancellation);
                var archive = ReadSarc(packed);
                var docs = new JsonObject();
                if (!candidates.ContainsKey(name) && archive.TryGetValue($"Component/ModelInfo/{name}.engine__component__ModelInfo.bgyml", out var modelInfo))
                {
                    var definition = new BymlReader(modelInfo).Root;
                    string? project = definition?["ModelProjectName"]?.GetValue<string>(), model = definition?["FmdbName"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(project) && !string.IsNullOrEmpty(model) && project.IndexOfAny(['/', '\\']) < 0 && model.IndexOfAny(['/', '\\']) < 0 && nup.Files.TryGetValue($"/Model/{project}.{model}.bfres.zs", out var modelEntry))
                        candidates[name] = [modelEntry];
                }
                {
                    foreach (var (member, data) in archive)
                    {
                        if (!member.EndsWith(".bgyml", StringComparison.OrdinalIgnoreCase) || !(member.Contains("BlackboardParamTable/", StringComparison.Ordinal) || member.Contains("ShapeParam/", StringComparison.Ordinal) || member.Contains("AreaParam/", StringComparison.Ordinal) || member.Contains("AIInfo/", StringComparison.Ordinal)))
                            continue;
                        try
                        {
                            docs[member] = new BymlReader(data).Root;
                        }
                        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException) { }
                    }
                    if (docs.Count > 0)
                        actorMetadata[name] = docs;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not IOException and not OutOfMemoryException and not AccessViolationException) { report.Add(new { actor = name, metadataError = ex.Message }); }
            if (actorDone % 25 == 0 || actorDone == actorTypes.Length)
                progress.Report($"Reading actor definitions: {actorDone:N0}/{actorTypes.Length:N0}");
        }
        var textureFiles = new Dictionary<string, string?>(StringComparer.Ordinal);
        int textureCount = 0;
        long textureBytes = 0;
        string? GetTexture(string name)
        {
            if (textureFiles.TryGetValue(name, out var cached))
                return cached;
            textureFiles[name] = null;
            if (name.IndexOfAny(['/', '\\']) >= 0 || !nup.Files.TryGetValue("/Tex/" + name + ".bntx.zs", out var entry))
                return null;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                byte[] data = Expand(nup.ReadMember(entry, cancellation), DictionaryFor(entry, "zs.zsdic"), cancellation);
                var texture = BntxTexture.Decode(data);
                if (textureBytes + texture.Data.Length > 512L * 1024 * 1024)
                    return null;
                textureBytes += texture.Data.Length;
                string file = $"t{textureCount++:D05}.tex";
                texture.Save(Path.Combine(geometry, file));
                textureFiles[name] = file;
                return file;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not IOException and not OutOfMemoryException and not AccessViolationException) { return null; }
        }
        var geometryIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        int done = 0, models = 0;
        foreach (var (actor, entries) in candidates.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            done++;
            if (entries.Count != 1)
            {
                report.Add(new
                {
                    actor,
                    error = "Ambiguous model name"
                });
                continue;
            }
            try
            {
                var entry = entries[0];
                byte[] asset = Expand(nup.ReadMember(entry, cancellation), DictionaryFor(entry, "zs.zsdic"), cancellation);
                string file = $"m{done:D5}.json";
                int triangles = WriteGeometry(asset, Path.Combine(geometry, file), GetTexture, cancellation);
                if (triangles > 0)
                {
                    geometryIndex.Add(actor, file);
                    models++;
                }
                report.Add(new
                {
                    actor,
                    triangles
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (EndOfStreamException ex) { report.Add(new { actor, error = ex.Message }); }
            catch (Exception ex) when (ex is not IOException and not OutOfMemoryException and not AccessViolationException) { report.Add(new { actor, error = ex.Message }); }
            if (done % 25 == 0 || done == candidates.Count)
                progress.Report($"Reading models: {done:N0}/{candidates.Count:N0}");
        }
        string terrain = TerrainScene.Prepare(nup, actual, geometry, e => Expand(nup.ReadMember(e, cancellation), DictionaryFor(e, "zs.zsdic"), cancellation), progress, cancellation);
        File.WriteAllText(Path.Combine(stage, "actor-types.json"), actorMetadata.ToJsonString());
        File.WriteAllText(Path.Combine(geometry, "index.json"), JsonSerializer.Serialize(geometryIndex));
        File.WriteAllText(Path.Combine(stage, "conversion-report.json"), JsonSerializer.Serialize(report));
        var document = new
        {
            format = "oot-scene-v1",
            name = actual.Name,
            game_title_id = "0400486024DEC000",
            game_version = nup.Version,
            placement_files = placementFiles,
            geometry_directory = "g",
            actor_metadata = "actor-types.json",
            geometry_matching = "actor name or actor package model reference",
            source = "local NUP",
            terrain,
            warnings = new[] { "Static placements and candidate triggers are not runtime state.", "Lighting is reconstructed. Full game materials, skeletal transforms and some model and texture formats are not decoded." }
        };
        File.WriteAllText(Path.Combine(stage, "scene.json"), JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        nup.VerifyUnchanged();
        cancellation.ThrowIfCancellationRequested();
        try
        {
            Directory.Move(stage, final);
        }
        catch (IOException) when (File.Exists(manifest)) { progress.Report("Using scene prepared by another instance."); return manifest; }
        progress.Report($"Prepared {models:N0} model types and {textureCount:N0} textures.");
        return manifest;
    }

    internal static byte[] Expand(byte[] compressed, byte[]? dictionary, CancellationToken cancellation)
    {
        using var input = new MemoryStream(compressed, false);
        using var stream = new DecompressionStream(input);
        if (dictionary != null)
            stream.LoadDictionary(dictionary);
        using var output = new MemoryStream();
        byte[] buffer = new byte[128 * 1024];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (output.Length > MaxExpanded - count)
                throw new InvalidDataException("Decompressed asset exceeds the safety limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    internal static Dictionary<string, byte[]> ReadSarc(byte[] data)
    {
        var b = data.AsSpan();
        if (b.Length < 32 || !b[..4].SequenceEqual("SARC"u8) || b[6] != 0xff || b[7] != 0xfe)
            throw new InvalidDataException("Invalid dictionary SARC.");
        int header = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(4, 2));
        int dataOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(12, 4)));
        if (header < 20 || header > b.Length - 12 || !b.Slice(header, 4).SequenceEqual("SFAT"u8))
            throw new InvalidDataException("Invalid SARC file table.");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(header + 6, 2)), nodes = checked(header + 12), names = checked(nodes + 16 * count + 8);
        if (names > b.Length || dataOffset > b.Length)
            throw new InvalidDataException("Invalid SARC bounds.");
        var output = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int p = nodes + i * 16;
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p + 4, 4));
            uint start = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p + 8, 4));
            uint end = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p + 12, 4));
            if ((flags & 0x01000000) == 0 || end < start || (ulong)dataOffset + end > (ulong)b.Length)
                throw new InvalidDataException("Invalid SARC member.");
            int nameOffset = checked(names + (int)(flags & 0xffffff) * 4);
            if (nameOffset < names || nameOffset >= dataOffset)
                throw new InvalidDataException("Invalid SARC filename.");
            int length = b.Slice(nameOffset, dataOffset - nameOffset).IndexOf((byte)0);
            if (length < 0)
                throw new InvalidDataException("Unterminated SARC filename.");
            string name = Encoding.UTF8.GetString(b.Slice(nameOffset, length));
            output.Add(name, b.Slice(checked(dataOffset + (int)start), checked((int)(end - start))).ToArray());
        }
        return output;
    }
    private static int WriteGeometry(byte[] asset, string output, Func<string, string?> getTexture, CancellationToken cancellation)
    {
        var resource = BfresGeometry.Read(asset);
        int triangles = 0;
        using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var json = new Utf8JsonWriter(file);
        json.WriteStartObject();
        json.WriteString("model", resource.FirstOrDefault()?.Name ?? "");
        json.WriteStartArray("shapes");
        foreach (var model in resource)
            foreach (var shape in model.Shapes)
            {
                cancellation.ThrowIfCancellationRequested();
                var position = shape.Positions;
                var uv = shape.Uvs;
                var normals = shape.Normals;
                var indices = shape.Indices;
                string? texture = null;
                if (uv != null && uv.Length == position.Length)
                {
                    var albedo = shape.Textures.FirstOrDefault(x => x.EndsWith("_Alb", StringComparison.OrdinalIgnoreCase));
                    if (albedo != null)
                        texture = getTexture(albedo);
                }
                json.WriteStartObject();
                json.WriteString("shape", shape.Name);
                if (texture != null)
                    json.WriteString("texture", texture);
                json.WriteStartArray("vertices");
                foreach (var point in position)
                {
                    json.WriteStartArray();
                    json.WriteNumberValue(point.X);
                    json.WriteNumberValue(point.Y);
                    json.WriteNumberValue(point.Z);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
                if (normals != null && normals.Length == position.Length)
                {
                    json.WriteStartArray("normals");
                    foreach (var point in normals)
                    {
                        json.WriteStartArray();
                        json.WriteNumberValue(point.X);
                        json.WriteNumberValue(point.Y);
                        json.WriteNumberValue(point.Z);
                        json.WriteEndArray();
                    }
                    json.WriteEndArray();
                }
                if (texture != null)
                {
                    json.WriteStartArray("uv");
                    foreach (var point in uv!)
                    {
                        json.WriteStartArray();
                        json.WriteNumberValue(point.X);
                        json.WriteNumberValue(point.Y);
                        json.WriteEndArray();
                    }
                    json.WriteEndArray();
                }
                json.WriteStartArray("indices");
                foreach (var index in indices)
                    json.WriteNumberValue(index);
                json.WriteEndArray();
                json.WriteEndObject();
                triangles += indices.Length / 3;
            }
        json.WriteEndArray();
        json.WriteEndObject();
        json.Flush();
        return triangles;
    }
}
