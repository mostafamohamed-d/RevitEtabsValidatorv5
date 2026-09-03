param(
  [string]$Configuration = "Release",
  [string]$RevitVersion = "2025",
  [string]$RevitInstallPath = ""
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RevitInstallPath)) {
    $RevitInstallPath = Join-Path $env:ProgramFiles ("Autodesk\Revit {0}" -f $RevitVersion)
}
$revitApiDll = Join-Path $RevitInstallPath 'RevitAPI.dll'
$revitApiUiDll = Join-Path $RevitInstallPath 'RevitAPIUI.dll'
if (!(Test-Path -LiteralPath $revitApiDll) -or !(Test-Path -LiteralPath $revitApiUiDll)) {
    throw "RevitAPI.dll / RevitAPIUI.dll not found under '$RevitInstallPath'. Pass -RevitInstallPath explicitly if Revit $RevitVersion is installed elsewhere."
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $projectRoot 'RevitEtabsValidator.csproj'
if (!(Test-Path -LiteralPath $project)) { throw "Project file not found: $project" }

$sourceApp = Join-Path $projectRoot 'Revit\App.cs'
if (!(Test-Path -LiteralPath $sourceApp)) { throw "Expected Revit application entry point was not found: $sourceApp" }
$sourceText = Get-Content -LiteralPath $sourceApp -Raw
if ($sourceText -notmatch 'class\s+App\s*:\s*IExternalApplication') {
    throw "Revit\App.cs does not contain the expected IExternalApplication entry point. Pull the latest repository revision before building."
}

Write-Host "Cleaning previous build output..." -ForegroundColor Cyan
dotnet clean $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE." }

# Do not manually remove bin\Release here. The compiled DLL may be loaded by a previous
# verification run in this same PowerShell process, which would make Remove-Item fail.
# dotnet clean/build already handles stale build artifacts safely.

Write-Host "Building RevitEtabsValidator ($Configuration)..." -ForegroundColor Cyan
dotnet build $project -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$dll = Join-Path $projectRoot ("bin\{0}\RevitEtabsValidator.dll" -f $Configuration)
if (!(Test-Path -LiteralPath $dll)) {
    throw "Build succeeded but DLL was not found at: $dll"
}

# Verify the compiled artifact without loading the actual build DLL into this PowerShell
# process. Loading the build output directly can lock it and break the next installer run.
$verifyRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RevitEtabsValidator-verify-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $verifyRoot | Out-Null
$verifyDll = Join-Path $verifyRoot 'RevitEtabsValidator.dll'
try {
    Copy-Item -LiteralPath $dll -Destination $verifyDll -Force

    # App (and ShowValidatorCommand indirectly) implements interfaces from
    # RevitAPIUI.dll. Resolving those types via reflection requires that
    # assembly to be resolvable, but it's intentionally NOT copied into the
    # build output (Private=false in the csproj, correctly - Revit supplies
    # it at runtime). Outside Revit's process, .NET can't find it anywhere,
    # so GetType() silently returns null and this step reports a false
    # "type not found" even when the DLL is fine. Copy both Revit reference
    # assemblies alongside the verification copy so resolution succeeds here
    # the same way it would inside Revit.
    Copy-Item -LiteralPath $revitApiDll -Destination (Join-Path $verifyRoot 'RevitAPI.dll') -Force
    Copy-Item -LiteralPath $revitApiUiDll -Destination (Join-Path $verifyRoot 'RevitAPIUI.dll') -Force

    $assembly = [System.Reflection.Assembly]::LoadFrom($verifyDll)
    $appType = $assembly.GetType('RevitEtabsValidator.App', $false, $false)
    $commandType = $assembly.GetType('RevitEtabsValidator.Revit.Commands.ShowValidatorCommand', $false, $false)

    if ($null -eq $appType) {
        throw "Compiled DLL does not contain RevitEtabsValidator.App. The local source/build is not the expected revision."
    }
    if ($null -eq $commandType) {
        throw "Compiled DLL does not contain RevitEtabsValidator.Revit.Commands.ShowValidatorCommand."
    }

    Write-Host "Artifact verified: RevitEtabsValidator.App and ShowValidatorCommand are present." -ForegroundColor Green
    Write-Host "Assembly identity: $($assembly.FullName)"
}
catch {
    throw "Compiled artifact verification failed: $($_.Exception.Message)"
}
finally {
    # The verification assembly may remain loaded, so only attempt cleanup best-effort.
    try { Remove-Item -LiteralPath $verifyRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}

$destRoot = Join-Path $env:APPDATA ("Autodesk\Revit\Addins\{0}" -f $RevitVersion)
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null

$destDll = Join-Path $destRoot 'RevitEtabsValidator.dll'
$destManifest = Join-Path $destRoot 'RevitEtabsValidator.addin'

Copy-Item -LiteralPath $dll -Destination $destDll -Force

$escapedDll = [System.Security.SecurityElement]::Escape($destDll)
$manifest = @"
<?xml version=""1.0"" encoding=""utf-8"" standalone=""no""?>
<RevitAddIns>
  <AddIn Type=""Application"">
    <Name>Revit ↔ ETABS Structural Model Validator</Name>
    <Assembly>$escapedDll</Assembly>
    <AddInId>8D3F9B8A-FA8C-4C4A-9A1D-0B1D5D8F6E73</AddInId>
    <FullClassName>RevitEtabsValidator.App</FullClassName>
    <VendorId>STRUCT-AUTO</VendorId>
    <VendorDescription>Structural model coordination validator</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $destManifest -Value $manifest -Encoding UTF8

# Final deployment verification without loading the live installed DLL. Copy it to a
# temporary location first so the deployed file is not locked by this PowerShell process.
$installedVerifyRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RevitEtabsValidator-installed-verify-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $installedVerifyRoot | Out-Null
$installedVerifyDll = Join-Path $installedVerifyRoot 'RevitEtabsValidator.dll'
try {
    $writtenManifest = Get-Content -LiteralPath $destManifest -Raw
    if ($writtenManifest -notmatch [regex]::Escape($escapedDll)) {
        throw "Manifest verification failed. Assembly path in $destManifest does not match $destDll"
    }
    if ($writtenManifest -match 'CouplingBeamVerifier') {
        throw "Manifest verification failed: CouplingBeamVerifier reference detected in $destManifest"
    }
    if (!(Test-Path -LiteralPath $destDll)) {
        throw "DLL verification failed: $destDll"
    }

    Copy-Item -LiteralPath $destDll -Destination $installedVerifyDll -Force
    Copy-Item -LiteralPath $revitApiDll -Destination (Join-Path $installedVerifyRoot 'RevitAPI.dll') -Force
    Copy-Item -LiteralPath $revitApiUiDll -Destination (Join-Path $installedVerifyRoot 'RevitAPIUI.dll') -Force
    $installedAssembly = [System.Reflection.Assembly]::LoadFrom($installedVerifyDll)
    if ($null -eq $installedAssembly.GetType('RevitEtabsValidator.App', $false, $false)) {
        throw "Installed DLL verification failed: RevitEtabsValidator.App is missing from $destDll"
    }

    Write-Host "Installed artifact verified: RevitEtabsValidator.App is present." -ForegroundColor Green
    Write-Host "Assembly identity: $($installedAssembly.FullName)"
}
finally {
    try { Remove-Item -LiteralPath $installedVerifyRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}

Write-Host ""
Write-Host "Installation complete." -ForegroundColor Green
Write-Host "DLL:      $destDll"
Write-Host "Manifest: $destManifest"
Write-Host ""
Write-Host "IMPORTANT: Close Revit 2025 completely before running this script again." -ForegroundColor Yellow
