# Upload all extracted files from a local FirstStepAppV2 folder.
# The target repository is PUBLIC. Review licensing and sensitive data before running.
$ErrorActionPreference = 'Stop'
$repo = 'https://github.com/renafcampos27/KEYENCE_CAMERA_DTM.git'
$source = $PSScriptRoot
$destination = Join-Path $env:TEMP 'KEYENCE_CAMERA_DTM_GitHub_Upload'
if (!(Get-Command git -ErrorAction SilentlyContinue)) { throw 'Install Git for Windows first.' }
if (Test-Path $destination) { throw "Remove the old staging folder after reviewing it: $destination" }
git clone $repo $destination
if ($LASTEXITCODE -ne 0) { throw 'git clone failed' }
robocopy $source $destination /E /XD .git /XF UPLOAD_TO_GITHUB.ps1 | Out-Null
if ($LASTEXITCODE -gt 7) { throw 'robocopy failed' }
Push-Location $destination
try {
    git add -A -f
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'No changes to publish.'
    } else {
        git commit -m 'Import complete unzipped FirstStepAppV2'
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed' }
        git push origin HEAD:main
        if ($LASTEXITCODE -ne 0) { throw 'git push failed' }
        Write-Host 'Published to https://github.com/renafcampos27/KEYENCE_CAMERA_DTM'
    }
} finally {
    Pop-Location
}
