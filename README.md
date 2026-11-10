# Vibryule

A Windows 3D scene viewer for the Ocarina of Time remake. Explore terrain and objects from your own local NUP, inspect actor data, and follow placement links.

**This is a vibe-coded project by Ultimation and ChatGPT.** It is an experimental viewer, not an editor or emulator.

## Get started

1. Install the **[.NET 10 Desktop Runtime — Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)**.
2. Download the ZIP from [Releases](https://github.com/Ultimationzzz/Vibryule/releases), extract it to a writable folder, and run `Vibryule.exe`.
3. Click **Open NUP**, select your local file, and choose a scene. First-time preparation may take a few minutes.

Windows x64 and Direct3D 11 are required. The .NET runtime is **not bundled**. No game files are included or downloaded; all assets come from your NUP. The viewer does not modify it.

Your NUP path is saved in `config.json` beside the app and reopened at startup. Prepared scenes are cached under `%LOCALAPPDATA%\Vibryule\cache`.

## Explore and inspect

- **WASD:** fly; **R/F:** up/down; **Shift/Ctrl:** faster/slower.
- **Left/right drag:** look around; **middle drag:** pan; **wheel or Z/X:** change speed.
- **Click an object:** select and highlight it. **Home** or **Focus object:** frame selection. **T:** top view.
- Browse object types in the tree. The inspector shows typed fields, flag values/defaults, indexed arrays, placement links and raw data.
- **Extent names** filters bounds and overlays by actor or placement name; commas mean “or.” Enable **Also filter models** to hide other models, or **Pick extents instead of surfaces** to select boxes.
- Toggle terrain, game textures, lighting, bounds and area overlays independently. Double-click a resolved inspector link to select its destination.

## Current limits

Hyrule Field and Kokiri Forest are tested; other scenes and game versions may be incomplete. Rendering uses original textures with reconstructed lighting shaders, not the full game renderer. Some models, animation, water and materials are unsupported.

Model bounds describe render geometry. Area boxes are **approximate**, not decoded collision volumes. Collision display is unavailable. Flags and links show stored resource data; live game state and the exact behavior of every trigger are not known.

## Build

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet build Vibryule.csproj
dotnet run --project tests/Vibryule.Checks.csproj
```

Parsing is C#. SpaceUI source is included; other dependencies restore from NuGet. See [test instructions](https://github.com/Ultimationzzz/Vibryule/blob/main/tests/README.md) for optional checks using local assets. Run `./publish.ps1` to create a framework-dependent release ZIP.

## License and credits

Vibryule uses the [PolyForm Noncommercial 1.0.0 license](LICENSE). Noncommercial use, modification and sharing are allowed with the required attribution to **Ultimation and ChatGPT**. Commercial use or sale requires separate permission. Third-party components retain their own licenses; see [CREDITS.md](CREDITS.md).

This is an unofficial project, unaffiliated with Nintendo. Game assets and trademarks belong to their respective owners.
