# Compila IPTV Recorder en un único ejecutable dentro de .\dist
# Requiere el SDK de .NET 10 (https://dotnet.microsoft.com/download)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet publish IptvRecorder.csproj -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o dist
Write-Host ""
Write-Host "Listo: $PSScriptRoot\dist\IptvRecorder.exe"
