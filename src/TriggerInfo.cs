using System.Text.Json;

namespace OotSceneViewer;

static class TriggerInfo
{
    internal static TreeNode[] Nodes(Actor actor, ActorGraph graph)
    {
        var nodes = new List<TreeNode>();
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
        nodes.Add(geometry);
        if (graph.FallKind(actor) != FallZoneKind.None)
        {
            var purpose = new TreeNode("Fall-zone evidence — runtime outcome unverified");
            foreach (var group in graph.Groups(actor).Where(g => ActorGraph.ClassifyFallModule(g.Logic) != FallZoneKind.None))
                purpose.Nodes.Add(group.Logic);
            purpose.Nodes.Add("FallDown and item-fall modules are classified separately. Death, damage and restart behavior are not decoded.");
            nodes.Add(purpose);
        }
        var actions = new TreeNode("Configured actions / conditions");
        AddParameters(actions, actor, graph);
        if (actions.Nodes.Count == 0)
            actions.Nodes.Add("No event/destination parameters recorded on this actor.");
        nodes.Add(actions);
        var related = graph.Links(actor).Select(l => l.Target)
            .Concat(graph.Groups(actor).SelectMany(g => g.References.Select(r => r.Actor)))
            .Where(a => a != null && !ReferenceEquals(a, actor)).Cast<Actor>().Distinct();
        var configured = new TreeNode("Related logic evidence — entry timing unverified");
        foreach (var peer in related)
        {
            if (!peer.Raw.TryGetProperty("Dynamic", out var values) || values.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var field in values.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(field.Value.GetString()))
                    continue;
                string? label = field.Name switch
                {
                    "RequestEventName" => "Requested event",
                    "FirstEventName" => "First event",
                    "InitEventName" => "Initial event",
                    "NextEventName" => "Next event",
                    "NextBancParamNameChild" => "Child destination",
                    "NextBancParamNameAdult" => "Adult destination",
                    "NextStartPosName" => "Destination start",
                    "InitEventFlag" => "Initial event flag",
                    _ => null
                };
                if (label != null)
                    configured.Nodes.Add(new TreeNode($"{label}: {field.Value.GetString()} [{peer.Name}]") { Tag = peer });
            }
        }
        if (configured.Nodes.Count > 0)
            nodes.Add(configured);
        var linked = new TreeNode("Linked actors — double-click to inspect");
        foreach (var link in graph.Links(actor))
        {
            var node = new TreeNode(link.ToString()) { Tag = link.Target };
            if (link.Target != null)
                AddParameters(node, link.Target, graph);
            linked.Nodes.Add(node);
        }
        nodes.Add(linked);
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
        nodes.Add(new TreeNode("Entry preview tests a point against displayed volumes. It does not run AI, evaluate live flags or execute events."));
        foreach (var node in nodes)
            node.Expand();
        return nodes.ToArray();
    }

    private static void AddParameters(TreeNode node, Actor actor, ActorGraph graph)
    {
        if (actor.Raw.TryGetProperty("Presence", out var presence))
            node.Nodes.Add(ActorInspector.ValueNode("Presence (live value unknown)", presence));
        var parameters = new List<(string Key, JsonElement Value, string Source)>();
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
                                parameters.Add((name, value, "type default"));
                        }
            }
        if (actor.Raw.TryGetProperty("Dynamic", out var dynamic) && dynamic.ValueKind == JsonValueKind.Object)
            foreach (var field in dynamic.EnumerateObject())
                parameters.Add((field.Name, field.Value, "placement"));
        foreach (var entry in parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (entry.Value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(entry.Value.GetString()))
                continue;
            // Retain raw names and values: a parameter name is not proof of runtime behavior.
            node.Nodes.Add(ActorInspector.ValueNode($"{entry.Key} ({entry.Source})", entry.Value));
        }
    }
}
