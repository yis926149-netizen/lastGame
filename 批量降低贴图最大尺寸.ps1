[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Backup
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$assetsRoot = Join-Path $projectRoot 'Assets'

# Target list is intentionally explicit so unrelated texture import settings are untouched.
$targets = @(
    @{ Path = 'UI/建筑血条.png'; Size = 512 },
    @{ Path = 'UI/近战血条.png'; Size = 512 },
    @{ Path = 'UI/射手血条.png'; Size = 512 },

    @{ Path = 'VFXPACK_IMPACT_WALLCOEUR_FreeVersion/03_Texture/ImpactValo3.png'; Size = 256 },
    @{ Path = 'VFXPACK_IMPACT_WALLCOEUR_FreeVersion/03_Texture/ImpactValo4.png'; Size = 256 },

    @{ Path = 'UI/barracks/01.png'; Size = 512 },
    @{ Path = 'UI/barracks/02.png'; Size = 512 },
    @{ Path = 'UI/barracks/03.png'; Size = 512 },
    @{ Path = 'UI/barracks/04.png'; Size = 512 },
    @{ Path = 'UI/barracks/05.png'; Size = 512 },
    @{ Path = 'UI/barracks/06.png'; Size = 512 },
    @{ Path = 'UI/barracks/07.png'; Size = 512 },
    @{ Path = 'UI/barracks/08.png'; Size = 512 },
    @{ Path = 'UI/barracks/09.png'; Size = 512 },

    @{ Path = 'UI/TalentCard/card-main/建筑.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-main/军事.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-main/经济.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-icon/shield.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-frame/三选一-蓝.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-frame/三选一-黄.png'; Size = 512 },
    @{ Path = 'UI/TalentCard/card-frame/三选一-紫.png'; Size = 512 },

    @{ Path = 'UI/Icon/卡槽-去卡牌位置版.png'; Size = 512 },
    @{ Path = 'UI/Icon/UI-继续.png'; Size = 512 },
    @{ Path = 'UI/Icon/军事.png'; Size = 512 },
    @{ Path = 'UI/Icon/Ranged.png'; Size = 512 },
    @{ Path = 'UI/Icon/Melee.png'; Size = 512 },
    @{ Path = 'UI/Icon/CityBuilderIcon.png'; Size = 512 },
    @{ Path = 'UI/Icon/Tech&CulturePoints.png'; Size = 512 },
    @{ Path = 'UI/Icon/金币+底部.png'; Size = 512 },
    @{ Path = 'UI/Icon/道具-金币.png'; Size = 512 },

    @{ Path = 'UI/Movment/EnemyUnit_Indicator.png'; Size = 512 },
    @{ Path = 'UI/Movment/Movement_Indicator.png'; Size = 512 },
    @{ Path = 'UI/TacticalCard/治疗术.png'; Size = 512 },
    @{ Path = 'UI/TacticalCard/指令.png'; Size = 512 },
    @{ Path = 'UI/UnitCards/archer.png'; Size = 512 },
    @{ Path = 'UI/UnitCards/swordsman.png'; Size = 512 },
    @{ Path = 'UI/BuildingCards/ArrowTower.png'; Size = 512 },
    @{ Path = 'UI/BuildingCards/barracks.png'; Size = 512 },
    @{ Path = 'UI/BuildingCards/占位.png'; Size = 512 },
    @{ Path = 'UI/WinBG.png'; Size = 1024 },
    @{ Path = 'UI/EndGameUI/DefeatBG.png'; Size = 1024 },
    @{ Path = 'UI/card.png'; Size = 512 },
    @{ Path = 'UI/unit.png'; Size = 512 },
    @{ Path = 'UI/gold.png'; Size = 512 },

    @{ Path = 'noise/noise.png'; Size = 512 },
    @{ Path = 'noise/water.png'; Size = 512 },
    @{ Path = 'Texture/highLand.jpg'; Size = 1024 },
    @{ Path = 'Texture/aerial_beach_01_diff_4k.jpg'; Size = 1024 },
    @{ Path = 'Materials/fogTest.png'; Size = 1024 },

    @{ Path = 'Toon_RTS/WesternKingdoms/models/Materials/textures/WK_Standard_Units.tga'; Size = 1024 },
    @{ Path = 'Toon_RTS/WesternKingdoms/models/Materials/textures/WK_Standard_Units_Red.png'; Size = 1024 }
)

function Get-MetaPath([string]$relativeAssetPath) {
    $assetPath = Join-Path $assetsRoot ($relativeAssetPath -replace '/', '\\')
    return @{ Asset = $assetPath; Meta = "$assetPath.meta" }
}

$changed = 0
$missing = 0
$already = 0
$errors = 0

foreach ($target in $targets) {
    $paths = Get-MetaPath $target.Path
    $assetPath = $paths.Asset
    $metaPath = $paths.Meta
    $newSize = [int]$target.Size

    if (-not (Test-Path $assetPath -PathType Leaf) -or -not (Test-Path $metaPath -PathType Leaf)) {
        Write-Warning "Missing asset or meta: $($target.Path)"
        $missing++
        continue
    }

    try {
        $content = [System.IO.File]::ReadAllText($metaPath)
        $matches = [regex]::Matches($content, '(?m)^(\s*maxTextureSize:\s*)(\d+)(\s*)$')
        if ($matches.Count -eq 0) {
            Write-Warning "No maxTextureSize entry found: $($target.Path)"
            $errors++
            continue
        }

        $fileChanged = $false
        $updated = [regex]::Replace(
            $content,
            '(?m)^(\s*maxTextureSize:\s*)(\d+)(\s*)$',
            {
                param($match)
                $oldSize = [int]$match.Groups[2].Value
                if ($oldSize -ne $newSize) { $script:fileChanged = $true }
                return $match.Groups[1].Value + $newSize + $match.Groups[3].Value
            }
        )

        if (-not $fileChanged) {
            Write-Output "UNCHANGED $newSize  $($target.Path)"
            $already++
            continue
        }

        if ($Apply) {
            if ($Backup) {
                Copy-Item -LiteralPath $metaPath -Destination "$metaPath.bak" -Force
            }
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
    Write-Output 'No files were modified. Re-run with -Apply to write the changes.'
} elseif ($Backup) {
    Write-Output 'Backups were written next to changed .meta files using the .meta.bak suffix.'
}
