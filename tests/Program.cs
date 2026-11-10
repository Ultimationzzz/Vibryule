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
                empty.CheckEmptyStartup();
            Console.WriteLine("PASS: empty startup has no bundled scene.");
            Console.WriteLine(ScrollChecks.Run());
            if (args.Length == 2 && args[0] == "--scene")
            {
                using var form = new Form1();
                form.SuppressRenderingForChecks();
                Console.WriteLine(form.RunUiChecks([args[1]]));
                var input = SceneLoader.ReadScene([args[1]]);
                using var view = new SceneView { SuppressRendering = true };
                view.SetActors(input.Actors, input.GeometryDirectory);
                using var host = new Form { ClientSize = new Size(640, 480), ShowInTaskbar = false };
                using var renderer = new Direct3DRenderer(host.Handle);
                renderer.SetWorld(view.World, view.Boxes, view.WorldRanges, view.BoxRanges);
                foreach (bool textures in new[] { true, false })
                    foreach (bool terrain in new[] { true, false })
                        renderer.Draw(640, 480, view.ViewProjection(640f / 480), [], 127, true, [], textures, terrain);
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
