param([string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Vibryule.csproj'
[xml]$metadata = Get-Content -LiteralPath $project
$version = [string]$metadata.Project.PropertyGroup.Version
$name = "Vibryule-$version-win-x64"
$output = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $name
$zip = "$output.zip"
if ((Test-Path -LiteralPath $output) -or (Test-Path -LiteralPath $zip)) {
    throw 'Release output already exists. Choose a fresh OutputRoot to avoid packaging stale files.'
}

dotnet publish $project -c Release -r win-x64 --self-contained false `
    -p:DebugType=none -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

foreach ($file in @('README.md', 'LICENSE', 'CREDITS.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $output
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $output -Recurse
$images = Join-Path $output 'docs/images'
New-Item -ItemType Directory -Path $images -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/images/vibryule.png') -Destination $images

$runtime = Get-Content -LiteralPath (Join-Path $output 'Vibryule.runtimeconfig.json') -Raw | ConvertFrom-Json
if ($runtime.runtimeOptions.includedFrameworks -or !$runtime.runtimeOptions.frameworks) {
    throw 'Expected a framework-dependent build without the .NET runtime.'
}
$forbidden = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object {
    $_.Name -match '^(AGENTS.*|config\.json|coreclr\.dll|hostfxr\.dll|hostpolicy\.dll|System\..*\.dll)$' -or
    $_.Extension -in @('.pdb', '.nup', '.nsp', '.xci', '.tex', '.bfres', '.bntx', '.zs', '.tmp', '.log')
}
if ($forbidden) { throw ('Forbidden release files: ' + ($forbidden.Name -join ', ')) }

Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
Write-Output $zip
Write-Output "SHA-256: $hash"
