# Genera el paquete para distribuir: release\IptvRecorder-vX.Y.Z-win-x64.zip
# Compilación autocontenida (no requiere instalar .NET) con ffmpeg.exe incluido.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$version = ([xml](Get-Content IptvRecorder.csproj)).Project.PropertyGroup.Version | Select-Object -First 1
$name = "IptvRecorder-v$version-win-x64"
$stage = Join-Path $PSScriptRoot "release\$name"
$zip = Join-Path $PSScriptRoot "release\$name.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

dotnet publish IptvRecorder.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:DebugType=none -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló" }

# ffmpeg: el del PATH o el del paquete de winget
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) {
    $ffmpeg = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ffmpeg) { throw "No se encontró ffmpeg.exe para incluir en el paquete" }
Copy-Item $ffmpeg (Join-Path $stage "ffmpeg.exe")
Copy-Item README.md $stage

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal

$mb = [math]::Round((Get-Item $zip).Length / 1MB)
Write-Host ""
Write-Host "Paquete listo: $zip ($mb MB)"
