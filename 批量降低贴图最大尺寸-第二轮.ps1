[CmdletBinding()]
param(
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$assetsRoot = Join-Path $projectRoot 'Assets'
$backupRoot = Join-Path $projectRoot '_meta_backup'

# Second round: textures still large in the latest build report.
$targets = @(
    @{ Path = 'UI/WinBG.png'; Size = 512 },
    @{ Path = 'UI/EndGameUI/DefeatBG.png'; Size = 512 },
    @{ Path = 'UI/goldmine.png'; Size = 512 },
    @{ Path = 'UI/map/test-1.png'; Size = 512 },
    @{ Path = 'UI/map/test-2.png'; Size = 512 },
    @{ Path = 'UI/map/map-1.png'; Size = 1024 },
    @{ Path = 'UI/map/map-2.png'; Size = 1024 },
    @{ Path = 'UI/map/map-3.png'; Size = 1024 },
    @{ Path = 'UI/map/map-4.png'; Size = 1024 },
    @{ Path = 'UI/map/map-5.png'; Size = 1024 },
    @{ Path = 'UI/WinText.png'; Size = 512 },
    @{ Path = 'UI/WinBtn-01.png'; Size = 512 },
    @{ Path = 'UI/WinBtn-02.png'; Size = 512 },
    @{ Path = 'UI/EndGameUI/textbox.png'; Size = 512 },
    @{ Path = 'UI/Icon/b1.png'; Size = 512 },
    @{ Path = 'UI/Icon/b2.png'; Size = 512 },
    @{ Path = 'UI/Icon/p1.png'; Size = 512 },
    @{ Path = 'UI/Icon/p2.png'; Size = 512 },
    @{ Path = 'UI/Icon/UI-倒计时.png'; Size = 256 },
    @{ Path = 'UI/Icon/UI-开始游戏按钮.png'; Size = 256 },
    @{ Path = 'UI/Icon/UI-暂停.png'; Size = 256 },
    @{ Path = 'UI/TalentCard/card-icon/sword.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-icon/heart.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-frame/奖励卡面-蓝.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-frame/奖励卡面-黄.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-frame/奖励卡面-紫.png'; Size = 512 },
    @{ Path = 'Materials/cover.png'; Size = 512 },
    @{ Path = 'KayKit/Packs/KayKit - Medieval Hexagon Pack (for Unity)/Textures/hexagons_medieval.png'; Size = 512 },
    @{ Path = 'KayKit/Packs/KayKit - Medieval Hexagon Pack (for Unity)/Textures/playerWall.png'; Size = 512 },
    @{ Path = 'KayKit/Packs/KayKit - Medieval Hexagon Pack (for Unity)/Textures/ai-wall.png'; Size = 512 },
    @{ Path = 'KayKit/Packs/KayKit - Medieval Hexagon Pack (for Unity)/Textures/GoldMine.png'; Size = 512 },
    @{ Path = 'unity-chan!/Unity-chan! Model/SplashScreen/Logo/Dark_Silhouette.png'; Size = 512 },
    @{ Path = 'Toon_RTS/WesternKingdoms/models/Materials/textures/WK_Standard_Units.tga'; Size = 512 },
    @{ Path = 'Toon_RTS/WesternKingdoms/models/Materials/textures/WK_Standard_Units_Red.png'; Size = 512 },
    @{ Path = 'Texture/flatLand.png'; Size = 512 },
    @{ Path = '_ImportArchive/RPG_Scene_Resources/Texture/WaterNormal01.png'; Size = 512 }
)

$changed = 0; $missing = 0; $already = 0; $errors = 0

foreach ($target in $targets) {
    $assetPath = Join-Path $assetsRoot ($target.Path -replace '/', '\\')
    $metaPath = "$assetPath.meta"
    $newSize = [int]$target.Size

    if (-not (Test-Path $assetPath -PathType Leaf) -or -not (Test-Path $metaPath -PathType Leaf)) {
        Write-Warning "Missing asset or meta: $($target.Path)"
        $missing++
        continue
    }

    try {
        $content = [System.IO.File]::ReadAllText($metaPath)
        if (-not ([regex]::IsMatch($content, '(?m)^\s*maxTextureSize:\s*\d+\s*$'))) {
            Write-Warning "No maxTextureSize entry found: $($target.Path)"
            $errors++
            continue
        }

        $script:fileChanged = $false
        $updated = [regex]::Replace(
            $content,
            '(?m)^(\s*maxTextureSize:\s*)(\d+)(\s*)$',
            {
                param($match)
                if ([int]$match.Groups[2].Value -ne $newSize) { $script:fileChanged = $true }
                return $match.Groups[1].Value + $newSize + $match.Groups[3].Value
            }
        )

        if (-not $fileChanged) {
            Write-Output "UNCHANGED $newSize  $($target.Path)"
            $already++
            continue
        }

        if ($Apply) {
            $relDir = Split-Path -Parent ($target.Path -replace '/', '\\')
            $backupDir = Join-Path $backupRoot $relDir
            New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
            Copy-Item -LiteralPath $metaPath -Destination (Join-Path $backupDir (Split-Path -Leaf $metaPath)) -Force
            [System.IO.File]::WriteAllText($metaPath, $updated, [System.Text.UTF8Encoding]::new($false))
            Write-Output "UPDATED   $newSize  $($target.Path)"
        } else {
            Write-Output "DRY-RUN   $newSize  $($target.Path)"
        }
        $changed++
    } catch {
        Write-Warning "Failed: $($target.Path) - $($_.Exception.Message)"
        $errors++
    }
}

Write-Output ''
Write-Output ("Targets: {0}; changed: {1}; already set: {2}; missing: {3}; errors: {4}" -f $targets.Count, $changed, $already, $missing, $errors)
if (-not $Apply) {
    Write-Output 'No files were modified. Re-run with -Apply to write the changes (backups go to _meta_backup).'
}
