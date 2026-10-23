# Builds all release artifacts for CircleQuickLauncher:
#   - Portable folder (self-contained win-x64)
#   - Inno Setup installer (requires Inno Setup 6.x at default path, skipped otherwise)
#   - MSIX package (unsigned; signed via CI secrets when available)
# Output: artifacts\{portable,installer,msix}
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifacts = Join-Path $root 'artifacts'
$version = '1.0.0'

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null

Write-Host '==> Restoring & building (Release x64)' -ForegroundColor Cyan
dotnet build (Join-Path $root 'CircleQuickLauncher.sln') -c Release -p:Platform=x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Write-Host '==> Running unit tests' -ForegroundColor Cyan
dotnet test (Join-Path $root 'tests\CircleQuickLauncher.Tests') -c Release --nologo --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

Write-Host '==> Publishing portable (self-contained win-x64)' -ForegroundColor Cyan
dotnet publish (Join-Path $root 'src\CircleQuickLauncher\CircleQuickLauncher.csproj') `
    -c Release -p:Platform=x64 `
    -p:PublishProfile=Portable `
    --nologo
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

# Zip the portable folder
$portableDir = Join-Path $artifacts 'portable'
$zipPath = Join-Path $artifacts "CircleQuickLauncher-$version-portable-win-x64.zip"
Write-Host "==> Creating portable zip: $zipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath -Force

# Inno Setup installer (optional — skipped if iscc not found)
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host '==> Building Inno Setup installer' -ForegroundColor Cyan
    & $iscc (Join-Path $root 'installer\InnoSetup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
} else {
    Write-Warning 'Inno Setup 6 (ISCC.exe) not found — skipping installer build.'
}

# MSIX package (unsigned unless cert provided via env)
Write-Host '==> Building MSIX package' -ForegroundColor Cyan
$msixDir = Join-Path $artifacts 'msix'
New-Item -ItemType Directory -Path $msixDir | Out-Null
$msixProps = @(
    '-p:WindowsPackageType=MSIX',
    '-p:GenerateAppxPackageOnBuild=true',
    '-p:AppxPackageSigningEnabled=false',
    "-p:AppxPackageDir=$msixDir\",
    '-p:UapAppxPackageBuildMode=SideloadOnly',
    '-p:AppxBundle=Never'
)
dotnet publish (Join-Path $root 'src\CircleQuickLauncher\CircleQuickLauncher.csproj') `
    -c Release -p:Platform=x64 @msixProps --nologo
if ($LASTEXITCODE -ne 0) { Write-Warning 'MSIX build failed (non-fatal for portable/installer output).' }

Write-Host ''
Write-Host "Done. Artifacts in: $artifacts" -ForegroundColor Green
Get-ChildItem $artifacts -Recurse -File | ForEach-Object {
    '  {0}  ({1:N0} bytes)' -f $_.FullName.Replace("$root\", ''), $_.Length
}
