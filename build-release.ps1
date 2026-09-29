# Builds every program as a standalone Windows app (no .NET install needed) and zips them
# into .\dist with the exact names Retro Launcher downloads from the GitHub release.
#
#   powershell -ExecutionPolicy Bypass -File .\build-release.ps1
#
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory $dist | Out-Null

$apps = @(
    @{ Project = "RetroRadio.csproj";                        Zip = "RetroRadio-win-x64.zip";          Extra = @("Demo - Game of Life.rdv") },
    @{ Project = "VideoConverter\RadioVideoConverter.csproj"; Zip = "RadioVideoConverter-win-x64.zip"; Extra = @() },
    @{ Project = "RadioDesigner\RadioDesigner.csproj";       Zip = "RadioDesigner-win-x64.zip";       Extra = @("Radio Templates") },
    @{ Project = "RetroLens\RetroLens.csproj";                Zip = "RetroLens-win-x64.zip";           Extra = @() },
    @{ Project = "RetroDash\RetroDash.csproj";                Zip = "RetroDash-win-x64.zip";           Extra = @() },
    @{ Project = "Launcher\RetroLauncher.csproj";            Zip = "RetroLauncher-win-x64.zip";       Extra = @() }
)

foreach ($app in $apps) {
    $name = [IO.Path]::GetFileNameWithoutExtension($app.Zip)
    $out = Join-Path $dist "build\$name"
    Write-Host "Publishing $($app.Project)..." -ForegroundColor Cyan
    dotnet publish (Join-Path $root $app.Project) -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none -p:Version=$Version -o $out --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publishing $($app.Project) failed." }
    foreach ($extra in $app.Extra) {
        $src = Join-Path $root $extra
        if (Test-Path $src) { Copy-Item -Recurse $src $out }
    }
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath (Join-Path $dist $app.Zip) -CompressionLevel Optimal
    $mb = [math]::Round((Get-Item (Join-Path $dist $app.Zip)).Length / 1MB, 1)
    Write-Host "  -> dist\$($app.Zip)  ($mb MB)" -ForegroundColor Green
}
# Retro Radio for macOS (Apple Silicon): the same radio code on Avalonia/SkiaSharp, packed as a .app.
Write-Host "Publishing Retro Radio for macOS (Apple Silicon)..." -ForegroundColor Cyan
$macOut = Join-Path $dist "build\mac"
dotnet publish (Join-Path $root "Mac\RetroRadio.Mac.csproj") -c Release -r osx-arm64 --self-contained true `
    -p:DebugType=none -p:Version=$Version -o $macOut --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Publishing the Mac version failed." }
dotnet run --project (Join-Path $root "Mac\Packager") -v quiet -- $macOut (Join-Path $root "Mac\RetroRadio.icns") `
    (Join-Path $dist "RetroRadio-macOS-arm64.zip") $Version (Join-Path $root "Demo - Game of Life.rdv")
if ($LASTEXITCODE -ne 0) { throw "Packaging the Mac app failed." }

# Retro Radio for Android (only when the Android workload is installed: dotnet workload install android).
if ((dotnet workload list) -match "\bandroid\b") {
    Write-Host "Publishing Retro Radio for Android..." -ForegroundColor Cyan
    $apkOut = Join-Path $dist "build\android"
    dotnet publish (Join-Path $root "Android\RetroRadio.Android.csproj") -c Release -f net10.0-android `
        -p:ApplicationDisplayVersion=$Version -o $apkOut --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publishing the Android version failed." }
    Copy-Item (Get-ChildItem $apkOut -Filter "*-Signed.apk" | Select-Object -First 1).FullName (Join-Path $dist "RetroRadio-android.apk")
    Write-Host "  -> dist\RetroRadio-android.apk" -ForegroundColor Green
} else {
    Write-Host "Skipping Android (no Android workload; GitHub Actions builds the .apk for releases)." -ForegroundColor DarkYellow
}

Remove-Item -Recurse -Force (Join-Path $dist "build")
Write-Host "`nDone. Upload everything in .\dist to a GitHub release tagged v$Version." -ForegroundColor Yellow
