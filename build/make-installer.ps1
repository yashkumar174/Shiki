# Builds Releases\ConvertedBroApp-win-Setup.exe: a per-user installer (no admin) made with Velopack.
# Usage:  powershell -ExecutionPolicy Bypass -File build\make-installer.ps1 [-Version 1.0.1]
param([string]$Version)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\ConvertedBro\ConvertedBro.csproj"
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

# The version lives in the project file unless given here.
if (-not $Version) { $Version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
Write-Host "Converted Bro $Version"

if (-not (Test-Path (Join-Path $root "tools\ffmpeg\ffmpeg.exe"))) { throw "tools\ffmpeg is missing: put the FFmpeg build there first (see README)." }

$publish = Join-Path $root "publish\installer"

# vpk unpacks the whole app into TEMP while packing (over 1 GB); keep that on this drive, not C:.
$temp = Join-Path $root "publish\tmp"
New-Item -ItemType Directory -Force $temp | Out-Null
$env:TEMP = $temp
$env:TMP = $temp
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& $dotnet publish $project -c Release -o $publish -p:Version=$Version -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# vpk is a .NET tool: dotnet tool install -g vpk
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
$vpk = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
if (-not (Test-Path $vpk)) { & $dotnet tool install -g vpk; if ($LASTEXITCODE -ne 0) { throw "could not install vpk" } }

& $vpk pack `
    --packId ConvertedBroApp `
    --packVersion $Version `
    --packDir $publish `
    --mainExe ConvertedBro.exe `
    --packTitle "Converted Bro" `
    --packAuthors "yashkumar172" `
    --icon (Join-Path $root "src\ConvertedBro\Assets\AppIcon.ico") `
    --splashImage (Join-Path $PSScriptRoot "splash.png") `
    --framework net8-x64-desktop `
    --shortcuts StartMenuRoot `
    --exclude "(.*\.pdb|DirectML\.Debug\.dll|.*\.lib)$" `
    --outputDir (Join-Path $root "Releases")
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
Get-ChildItem (Join-Path $root "Releases") | Format-Table Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
