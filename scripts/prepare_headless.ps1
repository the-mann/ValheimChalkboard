param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Root | Out-Null
$qaRoot = (Resolve-Path -LiteralPath $Root).Path
$steamDir = Join-Path $qaRoot 'steamcmd'
$serverDir = Join-Path $qaRoot 'server'
New-Item -ItemType Directory -Force -Path $steamDir | Out-Null
Invoke-WebRequest https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip -OutFile "$steamDir/steamcmd.zip"
Expand-Archive -LiteralPath "$steamDir/steamcmd.zip" -DestinationPath $steamDir -Force
# The first launch may only self-update. Retry and require actual server files.
for ($attempt = 0; $attempt -lt 3; $attempt++) {
    & "$steamDir/steamcmd.exe" +@sSteamCmdForcePlatformType windows +force_install_dir $serverDir +login anonymous +app_update 896660 validate +quit
    if ($LASTEXITCODE -eq 0 -and (Test-Path "$serverDir/valheim_server.exe")) { break }
}
if ($LASTEXITCODE -ne 0 -or !(Test-Path "$serverDir/valheim_server_Data/Managed/assembly_valheim.dll")) { throw 'SteamCMD server installation failed.' }
$packages = @(
    @{Owner='denikson';Name='BepInExPack_Valheim';Version='5.4.2351'},
    @{Owner='ValheimModding';Name='Jotunn';Version='2.30.2'},
    @{Owner='ValheimModding';Name='YamlDotNet';Version='16.3.1'}
)
foreach ($package in $packages) {
    $packageDir = Join-Path $qaRoot $package.Name
    $zip = "$packageDir.zip"
    Invoke-WebRequest "https://thunderstore.io/package/download/$($package.Owner)/$($package.Name)/$($package.Version)/" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $packageDir -Force
    if ($package.Name -eq 'BepInExPack_Valheim') {
        Copy-Item -Path "$packageDir/BepInExPack_Valheim/*" -Destination $serverDir -Recurse -Force
    } else {
        $destination = Join-Path $serverDir "BepInEx/plugins/$($package.Name)"
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Get-ChildItem -LiteralPath $packageDir -Filter '*.dll' -Recurse | Copy-Item -Destination $destination -Force
    }
}
# A headless runner must not create a visible BepInEx console window.
$configDir = Join-Path $serverDir 'BepInEx/config'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
"[Logging.Console]`nEnabled = false" | Set-Content -LiteralPath "$configDir/BepInEx.cfg"
Write-Output "Headless server ready: $serverDir"
