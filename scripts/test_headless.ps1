param(
    [Parameter(Mandatory=$true)][string]$ServerDir,
    [Parameter(Mandatory=$true)][string]$SaveDir,
    [int]$Port = 25456,
    [ValidateSet('Validate','Seed','Verify')][string]$Mode = 'Validate'
)
$ErrorActionPreference = 'Stop'
$serverRoot = (Resolve-Path -LiteralPath $ServerDir).Path
New-Item -ItemType Directory -Force -Path $SaveDir | Out-Null
$saveRoot = (Resolve-Path -LiteralPath $SaveDir).Path
$logPath = Join-Path $serverRoot 'chalkboard-validation.log'
$reports = Join-Path $serverRoot 'BepInEx/config/ValheimChalkboard/validation'
$started = Get-Date
$env:SteamAppId = '892970'
# Use only a disposable server installation and save directory, never production worlds.
$arguments = '-nographics -batchmode -name ChalkboardQA -port '+$Port+' -world ChalkboardHeadlessQA -password disposableqa -public 0 -savedir "'+$saveRoot+'" -logFile "'+$logPath+'" -chalkboard-validate'
if ($Mode -eq 'Seed') { $arguments += ' -chalkboard-world-seed' }
if ($Mode -eq 'Verify') { $arguments += ' -chalkboard-world-verify' }
$process = Start-Process -FilePath (Join-Path $serverRoot 'valheim_server.exe') -WorkingDirectory $serverRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$process.WaitForExit(240000)) { throw 'Headless engine validation timed out.' }
    $report = Get-ChildItem -LiteralPath $reports -Filter 'runtime-*.txt' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $started } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$report) { throw "No fresh engine report. Inspect $logPath" }
    $result = Get-Content -LiteralPath $report.FullName -Raw
    Write-Output $result
    if ($process.ExitCode -ne 0 -or $result.Contains('FAIL ') -or !$result.Contains('PASS runtime smoke suite;')) {
        throw "Headless engine validation failed (exit $($process.ExitCode))."
    }
} finally {
    $process.Refresh()
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
}
