param(
  [string]$RevitVersion = "2025"
)
$ErrorActionPreference = 'Stop'

$roots = @(
    (Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"),
    (Join-Path $env:ProgramData "Autodesk\Revit\Addins\$RevitVersion")
) | Select-Object -Unique

Write-Host "RevitEtabsValidator manifest diagnostic for Revit $RevitVersion" -ForegroundColor Cyan
Write-Host ""

foreach ($root in $roots) {
    Write-Host "Addins root: $root" -ForegroundColor DarkCyan
    if (!(Test-Path -LiteralPath $root)) {
        Write-Host "  NOT FOUND" -ForegroundColor Yellow
        continue
    }

    $files = Get-ChildItem -LiteralPath $root -Filter '*.addin' -File -ErrorAction SilentlyContinue
    if (!$files) {
        Write-Host "  No .addin files found."
        continue
    }

    foreach ($file in $files) {
        try {
            [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
            $nodes = $xml.RevitAddIns.AddIn
            foreach ($node in @($nodes)) {
                $name = [string]$node.Name
                $assembly = [string]$node.Assembly
                $className = [string]$node.FullClassName
                if ($name -match 'ETABS Structural Model Validator|RevitEtabsValidator' -or $className -match '^RevitEtabsValidator\.') {
                    Write-Host "  FILE:     $($file.FullName)" -ForegroundColor White
                    Write-Host "  Name:     $name"
                    Write-Host "  Assembly: $assembly" -ForegroundColor $(if($assembly -match 'CouplingBeamVerifier'){ 'Red' } else { 'Green' })
                    Write-Host "  Class:    $className"
                    Write-Host "  Assembly exists: $(Test-Path -LiteralPath $assembly)"
                    Write-Host ""
                }
            }
        }
        catch {
            Write-Host "  Could not parse $($file.FullName): $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

Write-Host "Expected assembly:" -ForegroundColor Cyan
$expected = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion\RevitEtabsValidator.dll"
Write-Host "  $expected"
Write-Host "Exists: $(Test-Path -LiteralPath $expected)"
