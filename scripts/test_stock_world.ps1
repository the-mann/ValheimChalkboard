param(
    [Parameter(Mandatory=$true)][string]$ServerDir,
    [Parameter(Mandatory=$true)][string]$MigratedSaveDir,
    [Parameter(Mandatory=$true)][string]$OutputDir,
    [int]$Port = 25456
)
$ErrorActionPreference = 'Stop'
$serverRoot = (Resolve-Path -LiteralPath $ServerDir).Path
$sourceSave = (Resolve-Path -LiteralPath $MigratedSaveDir).Path
if (Test-Path -LiteralPath $OutputDir) { throw 'Use a fresh stock-world output directory.' }
New-Item -ItemType Directory -Path $OutputDir | Out-Null
$stockRoot = (Resolve-Path -LiteralPath $OutputDir).Path
$loader = Join-Path $stockRoot 'BepInEx'
New-Item -ItemType Directory -Force "$loader/plugins" | Out-Null
Copy-Item -LiteralPath "$serverRoot/BepInEx/core" -Destination "$loader/core" -Recurse
New-Item -ItemType Directory -Force "$loader/config" | Out-Null
"[Logging.Console]`nEnabled = false" | Set-Content "$loader/config/BepInEx.cfg"
$project = Join-Path $PSScriptRoot '../tests/StockWorldProbe'
dotnet build $project -c Release "-p:GameManaged=$serverRoot/valheim_server_Data/Managed" "-p:BepInExDir=$loader"
if ($LASTEXITCODE -ne 0) { throw 'Stock observer build failed.' }
Copy-Item -LiteralPath "$project/bin/Release/netstandard2.1/StockWorldProbe.dll" -Destination "$loader/plugins/"
Copy-Item -LiteralPath $sourceSave -Destination "$stockRoot/saves" -Recurse
$reportPath = Join-Path $loader 'config/stock-world-report.txt'
$env:SteamAppId = '892970'
$arguments = '--doorstop-target-assembly "'+$loader+'/core/BepInEx.Preloader.dll" -batchmode -nographics -name ChalkboardStockQA -port '+$Port+' -world ChalkboardHeadlessQA -password disposableqa -public 0 -savedir "'+$stockRoot+'/saves" -logFile "'+$stockRoot+'/stock-engine.log"'
$process = Start-Process -FilePath "$serverRoot/valheim_server.exe" -WorkingDirectory $serverRoot -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (!$process.WaitForExit(240000)) { throw 'Stock world test timed out.' }
    if (!(Test-Path -LiteralPath $reportPath)) { throw "Stock observer produced no report; inspect $stockRoot/stock-engine.log" }
    $report = Get-Content -LiteralPath $reportPath -Raw
    Write-Output $report
    if ($process.ExitCode -ne 0 -or $report.Contains('FAIL ') -or !$report.Contains('PASS stock-world suite;')) { throw 'Stock world test failed.' }
} finally {
    $process.Refresh()
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
}
