param(
    [string]$Repo = "."
)

$ErrorActionPreference = "Stop"
$Repo = (Resolve-Path $Repo).Path
$Patch = Join-Path $PSScriptRoot "practice-duels-0.3.1-nearby-sims.patch"

Push-Location $Repo
try {
    if (-not (Test-Path ".git")) { throw "Not a git checkout: $Repo" }
    if (-not (Test-Path $Patch)) { throw "Patch file not found: $Patch" }

    $remote = git remote get-url origin 2>$null
    if ($LASTEXITCODE -ne 0 -or $remote -notmatch "forgetwhtuno/Erenshor-Duel") {
        throw "This does not look like the forgetwhtuno/Erenshor-Duel checkout."
    }

    $status = git status --porcelain
    if ($status) {
        throw "Working tree is not clean. Commit/stash your current changes before applying this patch."
    }

    Write-Host "Checking Practice Duels 0.3.1 patch..." -ForegroundColor Cyan
    git apply --check --whitespace=error-all $Patch
    if ($LASTEXITCODE -ne 0) {
        throw "git apply --check failed. No files were changed."
    }

    git apply --whitespace=fix $Patch
    if ($LASTEXITCODE -ne 0) {
        throw "git apply failed."
    }

    Write-Host "Patch applied." -ForegroundColor Green
    Write-Host "Run .\BUILD_AND_INSTALL.ps1 to compile against your installed Erenshor assemblies." -ForegroundColor Yellow
    Write-Host "Then run /eduel selftest in game before the live duel validation matrix." -ForegroundColor Yellow
    git status --short
}
finally {
    Pop-Location
}
