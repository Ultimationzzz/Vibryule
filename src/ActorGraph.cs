using System.Text;
using System.Text.Json;

namespace OotSceneViewer;

// Links and parameters are taken from placement records. They do not describe
// the current runtime state or prove how an event uses a referenced actor.
sealed class ActorGraph
{
    private readonly Dictionary<Actor, List<ActorRelationship>> links = new(ReferenceEqualityComparer.Instance);
    private readonly IReadOnlyDictionary<string, JsonElement> metadata;
    internal ActorGraph(IEnumerable<Actor> actors, IReadOnlyDictionary<string, JsonElement>? actorMetadata = null)
    {
        metadata = actorMetadata ?? new Dictionary<string, JsonElement>();
        var items = actors.ToArray();
        var byHash = items.Where(a => Hash(a.Raw, "Hash") != null).GroupBy(a => Hash(a.Raw, "Hash")!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var actor in items)
            links[actor] = [];
        foreach (var actor in items)
        {
            if (!actor.Raw.TryGetProperty("Links", out var rawLinks) || rawLinks.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var link in rawLinks.EnumerateArray())
            {
                var dst = Hash(link, "Dst");
                var src = Hash(link, "Src");
                if (dst == null)
                    continue;
                string kind = Text(link, "Name") ?? Text(link, "Gyaml") ?? "(unnamed link)";
                var destination = byHash.TryGetValue(dst.Value, out var matches) && matches.Length == 1 ? matches[0] : null;
                bool ambiguous = matches is { Length: > 1 };
                links[actor].Add(new ActorRelationship("Outgoing", kind, dst.Value, destination, ambiguous));
                if (destination != null)
                    links[destination].Add(new ActorRelationship("Incoming", kind, src ?? Hash(actor.Raw, "Hash") ?? 0, actor, false));
            }
        }
    }
    internal IReadOnlyList<ActorRelationship> Links(Actor actor) => links.TryGetValue(actor, out var values) ? values : [];
    internal JsonElement? Definitions(Actor actor) => metadata.TryGetValue(actor.Name, out var value) ? value : null;
    internal string Describe(Actor actor)
    {
        var b = new StringBuilder();
        b.AppendLine($"ACTOR  {actor.Name}");
        b.AppendLine($"Placement name: {Text(actor.Raw, "Name") ?? "(none)"}");
        b.AppendLine($"Hash: {Hash(actor.Raw, "Hash")?.ToString() ?? "(none)"}");
        b.AppendLine($"Source: {actor.Source}");
        b.AppendLine();
        if (actor.Raw.TryGetProperty("Presence", out var presence) && presence.ValueKind == JsonValueKind.Object)
        {
            b.AppendLine("PRESENCE DATA (stored condition; current value unknown)");
            foreach (var field in presence.EnumerateObject())
                b.AppendLine($"  {field.Name}: {Display(field.Value)}{Note(field.Name)}");
            b.AppendLine();
        }
        if (actor.Raw.TryGetProperty("Dynamic", out var dynamic) && dynamic.ValueKind == JsonValueKind.Object)
        {
            b.AppendLine("DYNAMIC PARAMETERS (as recorded)");
            foreach (var field in dynamic.EnumerateObject())
                b.AppendLine($"  {field.Name}: {Display(field.Value)}{Note(field.Name)}");
            b.AppendLine();
        }
        if (metadata.TryGetValue(actor.Name, out var definitions) && definitions.ValueKind == JsonValueKind.Object)
        {
            b.AppendLine("ACTOR TYPE DATA (shared defaults, from local actor package)");
            foreach (var document in definitions.EnumerateObject())
            {
                b.AppendLine($"  {document.Name}");
                if (document.Name.Contains("BlackboardParamTable/", StringComparison.Ordinal) && document.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var array in document.Value.EnumerateObject())
                        if (array.Value.ValueKind == JsonValueKind.Array)
                            foreach (var field in array.Value.EnumerateArray())
                            {
                                var key = Text(field, "BBKey");
                                if (key == null)
                                    continue;
                                string init = field.TryGetProperty("InitVal", out var val) || field.TryGetProperty("InitValConverted", out val) ? Display(val) : "(not recorded)";
                                string instance = field.TryGetProperty("IsInstanceParam", out var p) ? Display(p) : "unknown";
                                b.AppendLine($"    {key}  |  {array.Name}  |  default {init}  |  instance parameter {instance}");
                            }
                }
                else
                    b.AppendLine("    " + document.Value.GetRawText());
            }
            b.AppendLine();
        }
        var relations = Links(actor);
        b.AppendLine($"PLACEMENT LINKS ({relations.Count})");
        if (relations.Count == 0)
            b.AppendLine("  No explicit links in the loaded placement files.");
        foreach (var link in relations)
            b.AppendLine($"  {link}");
        b.AppendLine();
        b.AppendLine("Link names and flag values are file data; event behavior and trigger volumes are not confirmed.");
        b.AppendLine();
        b.AppendLine("RAW PLACEMENT JSON");
        b.AppendLine(JsonSerializer.Serialize(actor.Raw, new JsonSerializerOptions { WriteIndented = true }));
        return b.ToString();
    }
    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
    private static string Note(string key) => key switch
    {
        "FlagName" => "  [presence flag identifier; value unknown]",
        "IsNegation" => "  [negation setting; runtime evaluation unknown]",
        "IsCheckLazy" => "  [lazy-check setting; runtime behavior unknown]",
        "RequestEventName" => "  [configured event identifier]",
        "FirstEventName" or "InitEventName" or "NextEventName" or "AddEventName" => "  [configured event identifier; execution conditions unknown]",
        "InitEventFlag" or "AddEventFlag" or "IsAlternativeGameDataBool" => "  [game-data flag identifier; value and evaluation unknown]",
        "NextBancParamNameAdult" or "NextBancParamNameChild" => "  [configured destination scene identifier]",
        "NextStartPosName" => "  [configured destination start-position identifier]",
        "RigidBodyShapeType" => "  [shape-type code; numeric meanings not yet verified]",
        _ => ""
    };
    private static string? Text(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static ulong? Hash(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out var hash) ? hash : null;
}

record ActorRelationship(string Direction, string Kind, ulong Hash, Actor? Target, bool Ambiguous)
{
    public override string ToString() => $"{Direction} · {Kind} · {(Target == null ? Ambiguous ? "ambiguous" : "unresolved" : Target.Name)} [{Hash}]";
}
