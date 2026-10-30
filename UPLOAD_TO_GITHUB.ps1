# Upload all files in this extracted application to the GitHub repository.
# Requires Git for Windows, an authenticated GitHub login and push permissions.
# The destination repository is PUBLIC. Review binaries and configuration before running.
$ErrorActionPreference = 'Stop'
$repo = 'https://github.com/renafcampos27/KEYENCE_CAMERA_DTM.git'
$source = $PSScriptRoot
$destination = Join-Path $env:TEMP 'KEYENCE_CAMERA_DTM_GitHub_Upload'

if (!(Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is not installed. Install Git for Windows and run this script again.'
}
if (Test-Path $destination) {
    throw "Destination exists: $destination. Remove it after checking there is no pending work."
}
Write-Host 'Cloning GitHub repository...'
git clone $repo $destination
if ($LASTEXITCODE -ne 0) { throw 'Git clone failed.' }

Write-Host 'Copying all application files (including bin, obj, .vs and Keyence DLLs)...'
# /E includes all subfolders; /XD .git prevents copying repository internals.
robocopy $source $destination /E /XD .git /XF UPLOAD_TO_GITHUB.ps1 | Out-Null
if ($LASTEXITCODE -gt 7) { throw "Robocopy failed: $LASTEXITCODE" }

Push-Location $destination
try {
    git add -A -f
    if ($LASTEXITCODE -ne 0) { throw 'git add failed' }
    git diff --cached --stat
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Repository is already up to date.'
    } else {
        git commit -m 'Import full unzipped FirstStepAppV2 application'
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed: configure git user.name and user.email first' }
        git push origin HEAD:main
        if ($LASTEXITCODE -ne 0) { throw 'git push failed: check repository permissions and authentication' }
        Write-Host 'Upload complete: https://github.com/renafcampos27/KEYENCE_CAMERA_DTM'
    }
} finally {
    Pop-Location
}
