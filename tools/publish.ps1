# One-click build + package: read version -> run tests -> build -> zip (version-named) into publish\.
# ASCII only (so PowerShell reads it correctly regardless of codepage). Chinese filenames are handled via glob.
$ErrorActionPreference = "Stop"
# script lives in tools\, so go up one level to reach the repo root
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
Set-Location $root

# 1. Read version from src\VersionInfo.cs
$ver = "v2.0"
$m = Select-String -Path "src\VersionInfo.cs" -Pattern 'public const string Version'
if ($m) { if ($m.Line -match '"([^"]+)"') { $ver = $matches[1] } }
Write-Host "Version: $ver"

# 2. Run unit tests first; abort on failure
cmd /c "tests\run_tests.bat"
if ($LASTEXITCODE -ne 0) { Write-Host "Unit tests failed, aborting publish"; exit 1 }

# 3. Build (nopause: skip the interactive pause)
cmd /c "src\build.bat nopause"
if (-not (Test-Path "AutoClicker.exe")) { Write-Host "Build failed"; exit 1 }

# 4. Package
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }
New-Item -ItemType Directory -Path "publish" -Force | Out-Null
$outDir = "publish\AutoClickerTool-$ver"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Copy-Item "AutoClicker.exe" $outDir
if (Test-Path "README.md") { Copy-Item "README.md" $outDir }
if (Test-Path "README.en.md") { Copy-Item "README.en.md" $outDir }
if (Test-Path "docs\CHANGELOG.md") { Copy-Item "docs\CHANGELOG.md" -Destination (Join-Path $outDir "CHANGELOG.md") }
if (Test-Path "docs\screenshots") {
    New-Item -ItemType Directory -Path (Join-Path $outDir "docs") -Force | Out-Null
    Copy-Item "docs\screenshots" -Destination (Join-Path $outDir "docs\screenshots") -Recurse
}
Get-ChildItem -Path $root -Filter "*.txt" | Where-Object { $_.Name -ne "log.txt" } | ForEach-Object { Copy-Item $_.FullName $outDir }
if (Test-Path "interception.dll") { Copy-Item "interception.dll" $outDir }

$zip = "$outDir.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $outDir -DestinationPath $zip -Force

Write-Host ""
Write-Host "===== Published: $zip ====="
