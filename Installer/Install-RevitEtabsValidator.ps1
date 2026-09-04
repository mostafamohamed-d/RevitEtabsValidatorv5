param(
  [ValidateSet('Revit2024-ETABS21','Revit2025-ETABS22')]
  [string]$Target = 'Revit2024-ETABS21',
  [string]$Configuration = 'Release',
  [string]$RevitInstallPath = '',
  [string]$EtabsInstallPath = ''
)
$ErrorActionPreference = 'Stop'

switch ($Target) {
    'Revit2024-ETABS21' {
        $RevitVersion = '2024'
        $TargetFramework = 'net48'
        $EtabsVersion = '21'
    }
    'Revit2025-ETABS22' {
        $RevitVersion = '2025'
        $TargetFramework = 'net8.0-windows'
        $EtabsVersion = '22'
    }
}

if ([string]::IsNullOrWhiteSpace($RevitInstallPath)) {
    $RevitInstallPath = Join-Path $env:ProgramFiles ("Autodesk\Revit {0}" -f $RevitVersion)
}

if ([string]::IsNullOrWhiteSpace($EtabsInstallPath)) {
    $candidate = Join-Path $env:ProgramFiles ("Computers and Structures\ETABS {0}" -f $EtabsVersion)
    if (Test-Path -LiteralPath $candidate) {
        $EtabsInstallPath = $candidate
    } else {
        $candidateX86 = Join-Path ${env:ProgramFiles(x86)} ("Computers and Structures\ETABS {0}" -f $EtabsVersion)
        if (Test-Path -LiteralPath $candidateX86) { $EtabsInstallPath = $candidateX86 }
    }
}

$revitApiDll = Join-Path $RevitInstallPath 'RevitAPI.dll'
$revitApiUiDll = Join-Path $RevitInstallPath 'RevitAPIUI.dll'
$etabsApiDll = Join-Path $EtabsInstallPath 'ETABSv1.dll'

if (!(Test-Path -LiteralPath $revitApiDll) -or !(Test-Path -LiteralPath $revitApiUiDll)) {
    throw "RevitAPI.dll / RevitAPIUI.dll not found under '$RevitInstallPath'. Pass -RevitInstallPath explicitly."
}
if (!(Test-Path -LiteralPath $etabsApiDll)) {
    throw "ETABSv1.dll not found under '$EtabsInstallPath'. Pass -EtabsInstallPath explicitly or install ETABS $EtabsVersion."
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $projectRoot 'RevitEtabsValidator.csproj'
if (!(Test-Path -LiteralPath $project)) { throw "Project file not found: $project" }

$sourceApp = Join-Path $projectRoot 'Revit\App.cs'
if (!(Test-Path -LiteralPath $sourceApp)) { throw "Expected Revit application entry point was not found: $sourceApp" }
$sourceText = Get-Content -LiteralPath $sourceApp -Raw
if ($sourceText -notmatch 'class\s+App\s*:\s*IExternalApplication') {
    throw "Revit\App.cs does not contain the expected IExternalApplication entry point."
}

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host "Target:      $Target" -ForegroundColor Cyan
Write-Host "Revit:       $RevitVersion" -ForegroundColor Cyan
Write-Host "ETABS:       $EtabsVersion" -ForegroundColor Cyan
Write-Host "Framework:   $TargetFramework" -ForegroundColor Cyan
Write-Host "Revit path:  $RevitInstallPath" -ForegroundColor Cyan
Write-Host "ETABS path:  $EtabsInstallPath" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

Write-Host "Cleaning target build..." -ForegroundColor Cyan
dotnet clean $project -c $Configuration -f $TargetFramework --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed with exit code $LASTEXITCODE." }

Write-Host "Building $Target ($Configuration)..." -ForegroundColor Cyan
dotnet build $project -c $Configuration -f $TargetFramework --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$dll = Join-Path $projectRoot ("bin\{0}\{1}\RevitEtabsValidator.dll" -f $Configuration, $TargetFramework)
$builtEtabsDll = Join-Path $projectRoot ("bin\{0}\{1}\ETABSv1.dll" -f $Configuration, $TargetFramework)

if (!(Test-Path -LiteralPath $dll)) { throw "Build succeeded but DLL was not found at: $dll" }
if (!(Test-Path -LiteralPath $builtEtabsDll)) { throw "Build succeeded but ETABSv1.dll was not copied to: $builtEtabsDll" }

$verifyRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RevitEtabsValidator-verify-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $verifyRoot | Out-Null
$verifyDll = Join-Path $verifyRoot 'RevitEtabsValidator.dll'
try {
    Copy-Item -LiteralPath $dll -Destination $verifyDll -Force
    Copy-Item -LiteralPath $builtEtabsDll -Destination (Join-Path $verifyRoot 'ETABSv1.dll') -Force
    Copy-Item -LiteralPath $revitApiDll -Destination (Join-Path $verifyRoot 'RevitAPI.dll') -Force
    Copy-Item -LiteralPath $revitApiUiDll -Destination (Join-Path $verifyRoot 'RevitAPIUI.dll') -Force

    $assembly = [System.Reflection.Assembly]::LoadFrom($verifyDll)
    $appType = $assembly.GetType('RevitEtabsValidator.App', $false, $false)
    $commandType = $assembly.GetType('RevitEtabsValidator.Revit.Commands.ShowValidatorCommand', $false, $false)

    if ($null -eq $appType) { throw "Compiled DLL does not contain RevitEtabsValidator.App." }
    if ($null -eq $commandType) { throw "Compiled DLL does not contain ShowValidatorCommand." }

    Write-Host "Artifact verified: RevitEtabsValidator.App and ShowValidatorCommand are present." -ForegroundColor Green
    Write-Host "Assembly identity: $($assembly.FullName)"
}
finally {
    try { Remove-Item -LiteralPath $verifyRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}

$destRoot = Join-Path $env:APPDATA ("Autodesk\Revit\Addins\{0}" -f $RevitVersion)
New-Item -ItemType Directory -Force -Path $destRoot | Out-Null

$destDll = Join-Path $destRoot 'RevitEtabsValidator.dll'
$destEtabsDll = Join-Path $destRoot 'ETABSv1.dll'
$destManifest = Join-Path $destRoot 'RevitEtabsValidator.addin'

Copy-Item -LiteralPath $dll -Destination $destDll -Force
Copy-Item -LiteralPath $builtEtabsDll -Destination $destEtabsDll -Force

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

if (!(Test-Path -LiteralPath $destDll)) { throw "Installed DLL missing: $destDll" }
if (!(Test-Path -LiteralPath $destEtabsDll)) { throw "Installed ETABS API DLL missing: $destEtabsDll" }

Write-Host "" 
Write-Host "Installation complete." -ForegroundColor Green
Write-Host "Target:   $Target"
Write-Host "DLL:      $destDll"
Write-Host "ETABSv1:  $destEtabsDll"
Write-Host "Manifest: $destManifest"
Write-Host ""
Write-Host "Close and reopen Revit $RevitVersion before testing." -ForegroundColor Yellow
