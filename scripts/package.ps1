param(
    [Parameter(Mandatory=$true)][string]$Dll,
    [Parameter(Mandatory=$true)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dllPath = (Resolve-Path -LiteralPath $Dll).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputFile)
$stage = Join-Path $repo ("work/package-"+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force "$stage/BepInEx/plugins/ValheimWhiteboard" | Out-Null
New-Item -ItemType Directory -Force ([System.IO.Path]::GetDirectoryName($outputPath)) | Out-Null
Copy-Item -LiteralPath $dllPath -Destination "$stage/BepInEx/plugins/ValheimWhiteboard/ValheimWhiteboard.dll"
foreach ($file in @('README.md','CHANGELOG.md','LICENSE')) { Copy-Item -LiteralPath "$repo/$file" -Destination $stage }
foreach ($file in @('manifest.json','icon.png')) { Copy-Item -LiteralPath "$repo/thunderstore/$file" -Destination $stage }
Compress-Archive -Path "$stage/*" -DestinationPath $outputPath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($outputPath)
try {
    $dlls = @($zip.Entries | Where-Object { $_.FullName.EndsWith('.dll') })
    if ($dlls.Count -ne 1 -or $dlls[0].FullName -ne 'BepInEx/plugins/ValheimWhiteboard/ValheimWhiteboard.dll') { throw 'Package must contain only the production mod DLL.' }
    $stream = $dlls[0].Open()
    try { $sha = [System.Security.Cryptography.SHA256]::Create(); $packedHash = [Convert]::ToBase64String($sha.ComputeHash($stream)); $sha.Dispose() } finally { $stream.Dispose() }
    $stream = [System.IO.File]::OpenRead($dllPath)
    try { $sha = [System.Security.Cryptography.SHA256]::Create(); $sourceHash = [Convert]::ToBase64String($sha.ComputeHash($stream)); $sha.Dispose() } finally { $stream.Dispose() }
    if ($sourceHash -ne $packedHash) { throw 'Packaged DLL differs from the tested build.' }
    foreach ($file in @('manifest.json','README.md','icon.png','LICENSE')) { if (!$zip.GetEntry($file)) { throw "Missing package file: $file" } }
} finally { $zip.Dispose() }
Write-Output "Validated candidate package: $outputPath"
