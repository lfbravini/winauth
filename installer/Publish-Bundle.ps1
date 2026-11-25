#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\WorkstationMfa-Bundle"
$config = "Release"

Write-Host "Publishing WorkstationMfa bundle to $out"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$projects = @(
  "src\WorkstationMfa.Console\WorkstationMfa.Console.csproj",
  "src\WorkstationMfa.Cli\WorkstationMfa.Cli.csproj",
  "src\WorkstationMfa.Client\WorkstationMfa.Client.csproj"
)

foreach ($p in $projects) {
  $projPath = Join-Path $root $p
  $name = [IO.Path]::GetFileNameWithoutExtension($p)
  $dest = Join-Path $out $name
  Write-Host "Publishing $name..."
  dotnet publish $projPath -c $config -r win-x64 --self-contained false -o $dest
}

# Prefer Console publish as the primary runtime folder (engine + UI deps)
Get-ChildItem (Join-Path $out "WorkstationMfa.Console") | Copy-Item -Destination $out -Recurse -Force

# Overlay CLI + Client executables/deps
Copy-Item (Join-Path $out "WorkstationMfa.Cli\WorkstationMfa.Cli.exe") $out -Force
Copy-Item (Join-Path $out "WorkstationMfa.Cli\WorkstationMfa.Cli.dll") $out -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $out "WorkstationMfa.Cli\WorkstationMfa.Cli.deps.json") $out -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $out "WorkstationMfa.Cli\WorkstationMfa.Cli.runtimeconfig.json") $out -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $out "WorkstationMfa.Client\WorkstationMfa.Client.exe") $out -Force
Copy-Item (Join-Path $out "WorkstationMfa.Client\WorkstationMfa.Client.dll") $out -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $out "WorkstationMfa.Client\WorkstationMfa.Client.deps.json") $out -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $out "WorkstationMfa.Client\WorkstationMfa.Client.runtimeconfig.json") $out -Force -ErrorAction SilentlyContinue

# Profiles + docs
$profilesOut = Join-Path $out "profiles"
$docsOut = Join-Path $out "docs"
New-Item -ItemType Directory -Force -Path $profilesOut, $docsOut | Out-Null
Copy-Item (Join-Path $root "packages\profiles\*") $profilesOut -Force
Copy-Item (Join-Path $root "docs\*") $docsOut -Force
Copy-Item (Join-Path $root "README.md") $out -Force

# Horizon media if present
$horizonSrc = Join-Path $root "packages\horizon"
$horizonDst = Join-Path $out "packages\horizon"
New-Item -ItemType Directory -Force -Path $horizonDst | Out-Null
if (Test-Path $horizonSrc) {
  Get-ChildItem $horizonSrc -File | Where-Object { $_.Extension -in ".exe", ".msi" } | ForEach-Object {
    Copy-Item $_.FullName $horizonDst -Force
  }
}

Write-Host "Bundle ready: $out"
