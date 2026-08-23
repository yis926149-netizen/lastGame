[CmdletBinding()]
param(
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$audioRoot = Join-Path $projectRoot 'Assets\Auido'
$backupRoot = Join-Path $projectRoot '_meta_backup'

$changed = 0; $errors = 0; $scanned = 0

Get-ChildItem -Path $audioRoot -Recurse -Filter '*.wav' -File | ForEach-Object {
    $assetPath = $_.FullName
    $metaPath = "$assetPath.meta"
    if (-not (Test-Path $metaPath -PathType Leaf)) { return }
    $scanned++

    try {
        $content = [System.IO.File]::ReadAllText($metaPath)

        $loadTypeChanged = [regex]::IsMatch($content, '(?m)^\s*loadType:\s*0\s*$')
        $qualityChanged = [regex]::IsMatch($content, '(?m)^\s*quality:\s*1\s*$')
        if (-not $loadTypeChanged -and -not $qualityChanged) { return }

        $updated = [regex]::Replace($content, '(?m)^(\s*loadType:\s*)0(\s*)$', '${1}1${2}')
        $updated = [regex]::Replace($updated, '(?m)^(\s*quality:\s*)1(\s*)$', '${1}0.5${2}')

        $rel = $assetPath.Substring($audioRoot.Length + 1)
        if ($Apply) {
            $backupDir = Join-Path $backupRoot ('Auido\' + (Split-Path -Parent $rel))
            New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
            Copy-Item -LiteralPath $metaPath -Destination (Join-Path $backupDir (Split-Path -Leaf $metaPath)) -Force
            [System.IO.File]::WriteAllText($metaPath, $updated, [System.Text.UTF8Encoding]::new($false))
            Write-Output "UPDATED   $rel"
        } else {
            Write-Output "DRY-RUN   $rel"
        }
        $changed++
    } catch {
        Write-Warning "Failed: $assetPath - $($_.Exception.Message)"
        $errors++
    }
}

Write-Output ''
Write-Output ("Scanned: {0} wav; changed: {1}; errors: {2}" -f $scanned, $changed, $errors)
if (-not $Apply) {
    Write-Output 'No files were modified. Re-run with -Apply to write the changes (loadType 0->1, quality 1->0.5).'
}
