param(
    [string]$GameDir = "",
    [string]$LunarisLibDir = ""
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

function Find-LunarisLibDir([string]$Explicit, [string]$Game) {
    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    $candidates += (Join-Path $ScriptRoot "LunarisLibs")
    $candidates += $Game
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not $candidate) { continue }
        if ((Test-Path (Join-Path $candidate "Lunaris.dll")) -and (Test-Path (Join-Path $candidate "0Harmony.dll"))) {
            return (Resolve-Path $candidate).Path
        }
    }
    throw "Could not find Lunaris developer references. Put Lunaris.dll and 0Harmony.dll in '$ScriptRoot\LunarisLibs' or pass -LunarisLibDir."
}

function Find-Csc {
    foreach ($path in @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe", "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe")) {
        if (Test-Path $path) { return $path }
    }
    throw "csc.exe not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$GameDir = Find-Game $GameDir
$LunarisLibDir = Find-LunarisLibDir $LunarisLibDir $GameDir
$csc = Find-Csc
$managed = Join-Path $GameDir "Erenshor_Data\Managed"
$pluginDir = Join-Path $GameDir "plugins"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
$refs = @(
    (Join-Path $LunarisLibDir "Lunaris.dll"), (Join-Path $LunarisLibDir "0Harmony.dll"),
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
