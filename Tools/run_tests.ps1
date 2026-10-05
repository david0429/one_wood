# Runs the Unity EditMode tests in batch mode and prints a summary.
# Usage: Tools\run_tests.ps1 [-UnityEditor <path>] [extra Unity args, e.g. -testFilter ShotValidatorTests]
# The project must not be open in the editor.
param(
    [string]$UnityEditor = $env:UNITY_EDITOR,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$UnityArgs = @()
)
$ErrorActionPreference = "Stop"

$Repo = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Repo "OneWood"
$Version = (Select-String -Path (Join-Path $Project "ProjectSettings\ProjectVersion.txt") -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
if (-not $UnityEditor) { $UnityEditor = "C:\Program Files\Unity\Hub\Editor\$Version\Editor\Unity.exe" }
if (-not (Test-Path $UnityEditor)) { Write-Error "Unity $Version not found at $UnityEditor (pass -UnityEditor or set UNITY_EDITOR)"; exit 2 }

$Out = Join-Path $Project "Logs\tests"
$Results = Join-Path $Out "editmode-results.xml"
$Log = Join-Path $Out "editmode.log"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue $Results

Write-Host "Running EditMode tests with Unity $Version..."
$proc = Start-Process -FilePath $UnityEditor -Wait -PassThru -NoNewWindow -ArgumentList (@(
    "-batchmode", "-nographics", "-projectPath", "`"$Project`"",
    "-runTests", "-testPlatform", "EditMode", "-testResults", "`"$Results`"", "-logFile", "`"$Log`""
) + $UnityArgs)

if (-not (Test-Path $Results)) {
    Write-Host "No test results produced (exit $($proc.ExitCode)). See $Log" -ForegroundColor Red
    Select-String -Path $Log -Pattern "error CS|another Unity instance" | Select-Object -First 20 | ForEach-Object { $_.Line }
    exit 1
}

[xml]$xml = Get-Content $Results
$run = $xml."test-run"
Write-Host ("Result: {0}  total {1}  passed {2}  failed {3}  skipped {4}" -f $run.result, $run.total, $run.passed, $run.failed, $run.skipped)
$xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
    Write-Host "  FAILED $($_.fullname)" -ForegroundColor Red
    Write-Host "    $($_.failure.message.'#cdata-section')"
}
Write-Host "Results: $Results"
exit $proc.ExitCode
