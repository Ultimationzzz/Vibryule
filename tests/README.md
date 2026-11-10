# Checks

Run the synthetic selection, filtering and inspector checks on Windows:

```powershell
dotnet run --project tests/Vibryule.Checks.csproj
```

Optional checks use your own local assets. Paths below are placeholders:

```powershell
dotnet run --project tests/Vibryule.Checks.csproj -- --prepare "game.nup" "local-cache" HyruleField HyruleField_Child
dotnet run --project tests/Vibryule.Checks.csproj -- --scene "local-cache/path/scene.json"
dotnet run --project tests/Vibryule.Checks.csproj -- --terrain "game.nup" HyruleField
```

Scene checks exercise hierarchy, filters, picking, camera movement and live Direct3D rendering. Terrain checks validate tile heights, LOD coverage and shared edges. Keep assets, caches and test output outside the source repository.
