using System.Text.Json;

namespace OotSceneViewer;

static class TriggerInfo
{
    internal static TreeNode[] Nodes(Actor actor, ActorGraph graph)
    {
        var nodes = new List<TreeNode>();
        var conditions = new TreeNode("1 · Conditions (live state unknown)");
        conditions.Nodes.Add("Player-entry requirements are not decoded.");
        conditions.Nodes.Add("Presence settings describe availability, not entry.");
        if (actor.Raw.TryGetProperty("Presence", out var presence))
            conditions.Nodes.Add(ActorInspector.ValueNode("Presence settings", presence));
        foreach (var parameter in Parameters(actor, graph).Where(p => IsCondition(p.Key)))
            conditions.Nodes.Add(ParameterNode(parameter));
        nodes.Add(conditions);
        var configuredActions = new TreeNode("2 · Configured actions (timing unknown)");
        foreach (var parameter in Parameters(actor, graph).Where(p => ActionLabel(p.Key) != null && HasValue(p.Value)))
        {
            var node = ParameterNode(parameter);
            node.Text = ActionLabel(parameter.Key) + " · " + Display(parameter.Value);
            configuredActions.Nodes.Add(node);
        }
        if (configuredActions.Nodes.Count == 0)
            configuredActions.Nodes.Add("No recognized event or destination. Check linked actors.");
        configuredActions.Nodes.Add("Entering this volume is not proven to run these actions.");
        nodes.Add(configuredActions);
        var volumes = TriggerVolume.Read(actor, graph.Definitions(actor));
        var geometry = new TreeNode($"Inspection volumes ({volumes.Count})");
        foreach (var volume in volumes)
        {
            var shape = new TreeNode($"{volume.Shape} · {volume.Label}");
            shape.Nodes.Add(volume.Source);
            shape.Nodes.Add("World center: " + volume.Center.ToString("F2"));
            shape.Nodes.Add("Placement rotation and scale applied; runtime body transforms are not simulated.");
            geometry.Nodes.Add(shape);
        }
        if (volumes.Count == 0)
            geometry.Nodes.Add("No supported volume for this actor. Follow its area references below.");
        if (graph.FallKind(actor) != FallZoneKind.None)
        {
            var purpose = new TreeNode("Fall-zone evidence — runtime outcome unverified");
            foreach (var group in graph.Groups(actor).Where(g => ActorGraph.ClassifyFallModule(g.Logic) != FallZoneKind.None))
                purpose.Nodes.Add(group.Logic);
            purpose.Nodes.Add("FallDown and item-fall modules are classified separately. Death, damage and restart behavior are not decoded.");
            nodes.Add(purpose);
        }
        var actions = new TreeNode("All stored parameters and defaults");
        AddParameters(actions, actor, graph);
        if (actions.Nodes.Count == 0)
            actions.Nodes.Add("No event/destination parameters recorded on this actor.");
        var related = graph.Links(actor).Select(l => l.Target)
            .Concat(graph.Groups(actor).SelectMany(g => g.References.Select(r => r.Actor)))
            .Where(a => a != null && !ReferenceEquals(a, actor)).Cast<Actor>().Distinct();
        var configured = new TreeNode("Related action evidence (timing unknown)");
        foreach (var peer in related)
        {
            foreach (var field in Parameters(peer, graph))
            {
                if (!HasValue(field.Value))
                    continue;
                string? label = ActionLabel(field.Key);
                if (label != null)
                {
                    var node = ParameterNode(field);
                    node.Text = $"{label}: {Display(field.Value)} [{peer.Name}]";
                    node.Tag = peer;
                    node.Nodes.Add("Related actor evidence only. Follow to inspect its own conditions.");
                    configured.Nodes.Add(node);
                }
            }
        }
        var linked = new TreeNode($"3 · Linked actors ({graph.Links(actor).Count})");
        foreach (var link in graph.Links(actor))
        {
            var node = new TreeNode(link.ToString()) { Tag = link.Target };
            if (link.Target != null)
                AddParameters(node, link.Target, graph);
            linked.Nodes.Add(node);
        }
        nodes.Add(linked);
        if (linked.Nodes.Count == 0)
            linked.Nodes.Add("No explicit links. Check shared module references below.");
        else
            linked.Nodes.Add("Select a resolved row, then Follow selected link.");
        if (configured.Nodes.Count > 0)
            nodes.Add(configured);
        var logic = new TreeNode($"Shared logic modules ({graph.Groups(actor).Count})");
        foreach (var group in graph.Groups(actor))
        {
            var node = new TreeNode(group.Logic);
            node.Nodes.Add("References share a module; execution order and entry conditions are not decoded.");
            foreach (var reference in group.References)
            {
                var peer = new TreeNode($"{reference.Id} · {reference.Actor?.Name ?? "unresolved"} [{reference.Hash}]") { Tag = reference.Actor };
                if (reference.Actor != null && !ReferenceEquals(reference.Actor, actor))
                    AddParameters(peer, reference.Actor, graph);
                node.Nodes.Add(peer);
            }
            logic.Nodes.Add(node);
        }
        nodes.Add(logic);
        nodes.Add(geometry);
        nodes.Add(actions);
        nodes.Add(new TreeNode("Entry preview tests a point against displayed volumes. It does not run AI, evaluate live flags or execute events."));
        foreach (var node in nodes)
            if (!ReferenceEquals(node, actions))
                node.Expand();
        return nodes.ToArray();
    }

    private static void AddParameters(TreeNode node, Actor actor, ActorGraph graph)
    {
        if (actor.Raw.TryGetProperty("Presence", out var presence))
            node.Nodes.Add(ActorInspector.ValueNode("Presence (live value unknown)", presence));
        foreach (var entry in Parameters(actor, graph))
            node.Nodes.Add(ParameterNode(entry));
    }

    internal record Parameter(string Key, JsonElement Value, string Source, JsonElement? Default = null);

    internal static IReadOnlyList<Parameter> Parameters(Actor actor, ActorGraph graph)
    {
        var parameters = new List<Parameter>();
        var overridden = new HashSet<string>(StringComparer.Ordinal);
        if (graph.Definitions(actor) is { ValueKind: JsonValueKind.Object } docs)
            foreach (var doc in docs.EnumerateObject())
            {
                if (!doc.Name.Contains("BlackboardParamTable/") || doc.Value.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var array in doc.Value.EnumerateObject())
                    if (array.Value.ValueKind == JsonValueKind.Array)
                        foreach (var field in array.Value.EnumerateArray())
                        {
                            if (field.ValueKind != JsonValueKind.Object || !field.TryGetProperty("BBKey", out var key))
                                continue;
                            string? name = key.GetString();
                            if (name != null && (field.TryGetProperty("InitVal", out var value) || field.TryGetProperty("InitValConverted", out value)))
                            {
                                var initial = value;
                                string source = "Type default · " + doc.Name;
                                if (field.TryGetProperty("IsInstanceParam", out var instance) && instance.ValueKind == JsonValueKind.True &&
                                    actor.Raw.TryGetProperty("Dynamic", out var placementValues) && placementValues.ValueKind == JsonValueKind.Object && placementValues.TryGetProperty(name, out var supplied))
                                {
                                    value = supplied;
                                    source = "Placement override · Dynamic." + name;
                                    overridden.Add(name);
                                }
                                parameters.Add(new(name, value, source, initial));
                            }
                        }
            }
        if (actor.Raw.TryGetProperty("Dynamic", out var dynamic) && dynamic.ValueKind == JsonValueKind.Object)
            foreach (var field in dynamic.EnumerateObject())
                if (!overridden.Contains(field.Name))
                    parameters.Add(new(field.Name, field.Value, "Placement · Dynamic." + field.Name));
        return parameters.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
    }

    private static TreeNode ParameterNode(Parameter parameter)
    {
        var node = ActorInspector.ValueNode(parameter.Key, parameter.Value);
        node.ToolTipText = parameter.Source + "\n" + node.ToolTipText;
        node.Nodes.Add("Source: " + parameter.Source);
        if (parameter.Default is JsonElement initial)
            node.Nodes.Add(ActorInspector.ValueNode("Type default", initial));
        return node;
    }

    private static bool HasValue(JsonElement value) => value.ValueKind != JsonValueKind.Null &&
        (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));
    private static string Display(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
    // Label known identifiers, without inferring their execution from names or proximity.
    private static string? ActionLabel(string key) => key switch
    {
        "RequestEventName" => "Requested event",
        "FirstEventName" => "First event identifier",
        "InitEventName" => "Initial event identifier",
        "NextEventName" => "Next event identifier",
        "AddEventName" => "Additional event identifier",
        "NextBancParamNameChild" => "Destination scene (child)",
        "NextBancParamNameAdult" => "Destination scene (adult)",
        "NextStartPosName" => "Destination start position",
        _ => null
    };
    private static bool IsCondition(string key) => key.Contains("Flag", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("GameData", StringComparison.OrdinalIgnoreCase);
}
