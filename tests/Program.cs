namespace OotSceneViewer;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetColorMode(SystemColorMode.Dark);
            Console.WriteLine(SelectionChecks.Run());
            using (var empty = new Form1())
            {
                empty.CheckEmptyStartup();
                empty.CheckWindowMenu();
            }
            Console.WriteLine("PASS: empty startup has no bundled scene.");
            Console.WriteLine("PASS: themed menu, retained NUP session, current scene selection, chooser search and busy state.");
            Console.WriteLine(ScrollChecks.Run());
            Console.WriteLine(TriggerChecks.Run());
            Console.WriteLine(TerrainChecks.SignedHeightChecks());
            Console.WriteLine(BoneChecks.Run());
            Console.WriteLine(LightingChecks.Run());
            if (args.Length == 2 && args[0] == "--scene")
            {
                using var form = new Form1();
                form.SuppressRenderingForChecks();
                Console.WriteLine(form.RunUiChecks([args[1]]));
                var input = SceneLoader.ReadScene([args[1]]);
                var graph = new ActorGraph(input.Actors, input.ActorMetadata, input.LogicGroups);
                Console.WriteLine($"Scene logic: {input.LogicGroups?.Count ?? 0} modules, {input.Actors.Count(a => graph.Groups(a).Count > 0)} referenced actors.");
                using var view = new SceneView { SuppressRendering = true };
                view.SetActors(input.Actors, input.GeometryDirectory, graph);
                Console.WriteLine($"Emission: {view.WorldRanges.Count(r => r.EmissionTexture != null)} material ranges with original emission-color maps.");
                Console.WriteLine($"Water materials: {view.WorldRanges.Count(r => r.Water == WaterKind.Surface)} surfaces, {view.WorldRanges.Count(r => r.Water == WaterKind.Waterfall)} waterfalls, {view.WorldRanges.Count(r => r.Water == WaterKind.Volume)} volume proxies.");
                using var host = new Form { ClientSize = new Size(640, 480), ShowInTaskbar = false };
                using var renderer = new Direct3DRenderer(host.Handle);
                renderer.SetWorld(view.World, view.Boxes, view.WorldRanges, view.BoxRanges);
                foreach (bool textures in new[] { true, false })
                    foreach (bool terrain in new[] { true, false })
                        renderer.Draw(640, 480, view.ViewProjection(640f / 480), [], 127, true, [], textures, terrain);
                foreach (bool xray in new[] { false, true })
                    renderer.Draw(640, 480, view.ViewProjection(640f / 480), [], 127, false, [], zoneFaces: view.Zones.SelectMany(z => z.Faces).ToArray(), zoneLines: view.Zones.SelectMany(z => z.Lines).ToArray(), zoneXray: xray);
                renderer.SetWorld([], [], [], []);
                renderer.Draw(320, 240, System.Numerics.Matrix4x4.Identity, [], 127, false, []);
                Console.WriteLine("PASS: Direct3D textured/solid shaders, terrain toggles, bounds, resize and resource reset.");
            }
            else if (args.Length == 3 && args[0] == "--terrain")
                Console.WriteLine(TerrainChecks.Run(args[1], args[2]));
            else if (args.Length == 5 && args[0] == "--prepare")
            {
                var options = ScenePreparation.List(args[1], CancellationToken.None);
                var scene = options.Single(s => s.Folder == args[3] && s.Name == args[4]);
                var path = ScenePreparation.Prepare(args[1], scene, args[2], new Progress<string>(Console.WriteLine), CancellationToken.None);
                Console.WriteLine($"PASS: {options.Count} scenes; prepared {path}");
            }
            else if (args.Length != 0)
                throw new ArgumentException("Use --scene <scene.json>, --terrain <nup> <terrain-name>, or --prepare <nup> <cache> <folder> <scene>.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
