# build.ps1 - Local build script for PdfStamperLibrary
# Usage:
#   .\build.ps1          # Standard build
#   .\build.ps1 -Clean   # Clean build (removes bin/obj first)

param(
    [switch]$Clean
)

$ProjectFile = "PdfStamperLibrary.csproj"
$ArtifactName = "PdfStamperLibrary"
$ZipFile = "$ArtifactName.zip"

Write-Host "========================================"
Write-Host " PdfStamper - Local Build"
Write-Host "========================================"

if ($Clean) {
    Write-Host "`n[CLEAN] Removing bin/ obj/ publish/ ..."
    Remove-Item -Recurse -Force bin, obj, publish -ErrorAction SilentlyContinue
    Write-Host "Clean complete."
}

# Restore
Write-Host "`n[1/4] Restoring NuGet packages..."
dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) { Write-Error "Restore failed"; exit 1 }

# Build
Write-Host "`n[2/4] Building (Release)..."
dotnet build $ProjectFile --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed"; exit 1 }

# Publish
Write-Host "`n[3/4] Publishing for linux-x64..."
dotnet publish $ProjectFile `
    --configuration Release `
    --runtime linux-x64 `
    --output ./publish `
    --no-build
if ($LASTEXITCODE -ne 0) { Write-Error "Publish failed"; exit 1 }

# Package
Write-Host "`n[4/4] Creating ZIP..."
if (Test-Path $ZipFile) { Remove-Item $ZipFile -Force }
Compress-Archive -Path ./publish/* -DestinationPath $ZipFile -Force

$zipSize = (Get-Item $ZipFile).Length / 1MB
Write-Host "`n========================================"
Write-Host " BUILD COMPLETE"
Write-Host "   Package : $ZipFile"
Write-Host "   Size    : $($zipSize.ToString('F2')) MB"
Write-Host "   Ready to upload to ODC Portal"
Write-Host "========================================"
