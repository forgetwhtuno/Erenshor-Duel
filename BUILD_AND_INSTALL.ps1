param(
    [string]$GameDir = "",
    [string]$BepInExRoot = ""
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceHead = "unavailable"
try { $sourceHead = (& git -C $ScriptRoot rev-parse HEAD).Trim() } catch { }

function Find-Game([string]$Explicit) {
    if ($Explicit -and (Test-Path (Join-Path $Explicit "Erenshor.exe"))) { return (Resolve-Path $Explicit).Path }
    $candidates = @()
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} "Steam\steamapps\common\Erenshor" }
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles "Steam\steamapps\common\Erenshor" }
    foreach ($root in @((Join-Path ${env:ProgramFiles(x86)} "Steam"), (Join-Path $env:ProgramFiles "Steam"))) {
        $vdf = Join-Path $root "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object {
                $library = $_.Groups[1].Value -replace '\\\\','\'
                $candidates += [IO.Path]::Combine($library, "steamapps", "common", "Erenshor")
            }
        }
    }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path (Join-Path $candidate "Erenshor.exe")) { return (Resolve-Path $candidate).Path }
    }
    throw "Erenshor installation not found. Pass -GameDir 'C:\path\to\Erenshor'."
}

function Find-Roots([string]$Explicit, [string]$Game) {
    if ($Explicit -and (Test-Path (Join-Path $Explicit "BepInEx\core\BepInEx.dll"))) {
        return ,(Resolve-Path $Explicit).Path
    }
    $roots = @()
    if (Test-Path (Join-Path $Game "BepInEx\core\BepInEx.dll")) { $roots += (Resolve-Path $Game).Path }
    foreach ($parent in @((Join-Path $env:APPDATA "r2modmanPlus-local\Erenshor\profiles"), (Join-Path $env:APPDATA "Thunderstore Mod Manager\DataFolder\Erenshor\profiles"))) {
        if (Test-Path $parent) {
            Get-ChildItem $parent -Directory | ForEach-Object {
                if (Test-Path (Join-Path $_.FullName "BepInEx\core\BepInEx.dll")) { $roots += $_.FullName }
            }
        }
    }
    @($roots | Select-Object -Unique)
}

function Find-Csc {
    foreach ($path in @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe", "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe")) {
        if (Test-Path $path) { return $path }
    }
    throw "csc.exe not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$GameDir = Find-Game $GameDir
$roots = @(Find-Roots $BepInExRoot $GameDir)
if ($roots.Count -eq 0) { throw "No BepInEx profile found. Launch Erenshor modded once, then rerun this script." }
if ($roots.Count -gt 1) {
    for ($i = 0; $i -lt $roots.Count; $i++) { Write-Host ("[{0}] {1}" -f $i, $roots[$i]) }
    $index = [int](Read-Host "Choose profile number (0-$($roots.Count - 1))")
    if ($index -lt 0 -or $index -ge $roots.Count) { throw "Invalid profile number: $index" }
    $InstallRoot = $roots[$index]
} else { $InstallRoot = $roots[0] }

$csc = Find-Csc
$managed = Join-Path $GameDir "Erenshor_Data\Managed"
$core = Join-Path $InstallRoot "BepInEx\core"
$pluginDir = Join-Path $InstallRoot "BepInEx\plugins\ErenshorDuel"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
$refs = @(
    (Join-Path $core "BepInEx.dll"), (Join-Path $core "0Harmony.dll"),
    (Join-Path $managed "Assembly-CSharp.dll"), (Join-Path $managed "netstandard.dll"),
    (Join-Path $managed "UnityEngine.dll"), (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.UI.dll")
)
foreach ($ref in $refs) { if (-not (Test-Path $ref)) { throw "Missing reference: $ref" } }
$out = Join-Path $pluginDir "ErenshorDuel.dll"
$rsp = Join-Path $env:TEMP "ErenshorDuel.rsp"
$lines = @('/nologo','/target:library','/optimize+',('/out:"{0}"' -f $out))
$refs | ForEach-Object { $lines += ('/reference:"{0}"' -f $_) }
Get-ChildItem (Join-Path $ScriptRoot "src") -Filter "*.cs" | ForEach-Object { $lines += '"' + $_.FullName + '"' }
$lines | Set-Content $rsp -Encoding ASCII
& $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "Compilation failed." }
$installed = Get-Item -LiteralPath $out
$hash = (Get-FileHash -LiteralPath $out -Algorithm SHA256).Hash
Write-Host "Installed Erenshor Practice Duels to $out" -ForegroundColor Green
Write-Host "BUILD_AND_INSTALL source HEAD: $sourceHead"
Write-Host "BUILD_AND_INSTALL output DLL: $out"
Write-Host "BUILD_AND_INSTALL installed DLL: $($installed.FullName)"
Write-Host "BUILD_AND_INSTALL timestamp UTC: $($installed.LastWriteTimeUtc.ToString('o'))"
Write-Host "BUILD_AND_INSTALL file size: $($installed.Length)"
Write-Host "BUILD_AND_INSTALL SHA256: $hash"
