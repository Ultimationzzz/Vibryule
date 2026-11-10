# Checks

Run the selection, filtering, inspector, themed scrollbar and trigger-volume checks on Windows with Direct3D 11 available:

```powershell
dotnet run --project tests/Vibryule.Checks.csproj
```

Optional checks use your own local assets. Paths below are placeholders:

```powershell
dotnet run --project tests/Vibryule.Checks.csproj -- --prepare "game.nup" "local-cache" HyruleField HyruleField_Child
dotnet run --project tests/Vibryule.Checks.csproj -- --scene "local-cache/path/scene.json"
dotnet run --project tests/Vibryule.Checks.csproj -- --terrain "game.nup" HyruleField
```

The default checks also render offscreen pixels to verify lighting, exposure, haze, wireframe, translucent zone depth/x-ray and graphics-state reset. They create native control handles but do not automate the desktop.

Scene checks exercise hierarchy, zone filters/picking, logic references, camera movement and Direct3D rendering with local models, textures and zone overlays. Terrain checks validate tile heights, LOD coverage and shared edges. Keep assets, caches and test output outside the source repository.
