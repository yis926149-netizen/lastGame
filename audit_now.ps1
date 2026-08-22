$ErrorActionPreference = 'Continue'
$root = 'E:\BaiduNetdiskDownload\毕设\My project - new\Assets'
Write-Output 'Scanning meta files...'
$map = @{}
Get-ChildItem -Path $root -Recurse -Filter '*.meta' -File | ForEach-Object {
    $p = $_.FullName
    $assetPath = $p.Substring(0, $p.Length - 5)
    if (-not (Test-Path $assetPath -PathType Leaf)) { return }
    $reader = New-Object System.IO.StreamReader($p)
    try {
        for ($i = 0; $i -lt 4; $i++) {
            $line = $reader.ReadLine()
            if ($null -eq $line) { break }
            if ($line -match '^guid:\s*([0-9a-fA-F]{32})') {
                $map[$Matches[1].ToLower()] = $assetPath
                break
            }
        }
    } finally { $reader.Close() }
}
Write-Output ("GUID map: {0}" -f $map.Count)

$seedGuids = @(
 '9fc0d4010bbf28b4594072e72b8655ab', # GameScene.unity
 '41f3c85f192600b459ca415533041ece', # StartScene.unity
 'c3443cc91121f3c42a8c6728de881c31', # UnitDatabase
 '0bfebc4b346137d45a6303df32ba1987', # BuildingDatabase
 'aa1f9dac6012b154e8b8c3753aea36e8', # PublicBuildingDatabaseSO
 '2ba0eff9153800544abde9aefc750fe1', # UIConfig
 '0a7ad5142d1fce34fb79cad5ae6024f5', # MapGenerationConfig
 '0fbceb72dd74f114e91ae3bddc87afbb', # Talent pool
 '29cf21858dbe05c48860a0f62dc46634', # NormalCardPool
 'a8eda5217d697e64696d242d03841dfd', # ExplorationRewardConfigSO
 '71a67a3257b36b047bc6280d46aa59f0', # TacticalCardDatabase
 '3457a3a7396f4063ad6a02b4b64c844a', # MapResourceDatabase
 '3760f0f9ca8c4482bdf979b2841c527b'  # MapLandFormDatabase
)

$traverseExts = @('.unity','.prefab','.mat','.asset','.controller','.anim','.overridecontroller','.playable','.signal','.mixer','.fontsettings','.physicmaterial','.spriteatlas','.lighting')

$queue = New-Object System.Collections.Queue
foreach ($g in $seedGuids) { $queue.Enqueue($g) }
# seed everything under Assets/Resources (force-included by Unity)
$resDir = Join-Path $root 'Resources'
if (Test-Path $resDir) {
    Get-ChildItem -Path $resDir -Recurse -File | ForEach-Object {
        $mp = $_.FullName + '.meta'
        if (Test-Path $mp) {
            $r = New-Object System.IO.StreamReader($mp)
            try {
                for ($i = 0; $i -lt 4; $i++) {
                    $line = $r.ReadLine()
                    if ($null -eq $line) { break }
                    if ($line -match '^guid:\s*([0-9a-fA-F]{32})') { $queue.Enqueue($Matches[1].ToLower()); break }
                }
            } finally { $r.Close() }
        }
    }
}

$seen = @{}
$guidRe = [regex]'guid:\s*([0-9a-fA-F]{32})'
$count = 0
while ($queue.Count -gt 0) {
    $g = $queue.Dequeue()
    if ($seen.ContainsKey($g)) { continue }
    $seen[$g] = $true
    if (-not $map.ContainsKey($g)) { continue }
    $path = $map[$g]
    if (-not (Test-Path $path -PathType Leaf)) { continue }
    $ext = [System.IO.Path]::GetExtension($path).ToLower()
    if ($traverseExts -contains $ext) {
        $content = [System.IO.File]::ReadAllText($path)
        foreach ($m in $guidRe.Matches($content)) {
            $t = $m.Groups[1].Value.ToLower()
            if ($map.ContainsKey($t) -and -not $seen.ContainsKey($t)) { $queue.Enqueue($t) }
        }
    }
    $count++
    if ($count % 500 -eq 0) { Write-Output ("...{0} nodes" -f $count) }
}
Write-Output ("Nodes visited: {0}" -f $count)

$total = 0; $n = 0
$buckets = @{}; $exts = @{}
$files = New-Object System.Collections.Generic.List[object]
foreach ($g in $seen.Keys) {
    if ($map.ContainsKey($g)) {
        $p = $map[$g]
        if (Test-Path $p -PathType Leaf) {
            $len = (Get-Item $p).Length
            $total += $len; $n++
            $rel = $p.Substring($root.Length + 1)
            $parts = $rel.Split([char]'\')
            $bucket = if ($parts[0] -eq '_ImportArchive' -and $parts.Count -ge 2) { '_ImportArchive\' + $parts[1] } else { $parts[0] }
            if (-not $buckets.ContainsKey($bucket)) { $buckets[$bucket] = @{ bytes = [long]0; count = 0 } }
            $buckets[$bucket].bytes += $len
            $buckets[$bucket].count++
            $ext = [System.IO.Path]::GetExtension($p).ToLower()
            if ($ext -eq '') { $ext = '(none)' }
            if (-not $exts.ContainsKey($ext)) { $exts[$ext] = @{ bytes = [long]0; count = 0 } }
            $exts[$ext].bytes += $len
            $exts[$ext].count++
            $files.Add([pscustomobject]@{ Path = $rel; Len = $len })
        }
    }
}
Write-Output ''
Write-Output ("REACHABLE TOTAL: {0} files, {1:N2} MB" -f $n, ($total/1MB))
Write-Output ''
Write-Output '--- BY BUCKET ---'
$buckets.GetEnumerator() | Sort-Object { $_.Value.bytes } -Descending | ForEach-Object {
    Write-Output ("{0,-42} {1,10:N2} MB  n={2}" -f $_.Key, ($_.Value.bytes/1MB), $_.Value.count)
}
Write-Output ''
Write-Output '--- BY EXTENSION ---'
$exts.GetEnumerator() | Sort-Object { $_.Value.bytes } -Descending | ForEach-Object {
    Write-Output ("{0,-12} {1,10:N2} MB  n={2}" -f $_.Key, ($_.Value.bytes/1MB), $_.Value.count)
}
Write-Output ''
Write-Output '--- TOP 50 FILES ---'
$files | Sort-Object Len -Descending | Select-Object -First 50 | ForEach-Object {
    Write-Output ("{0,10:N2} MB  {1}" -f ($_.Len/1MB), $_.Path)
}
Write-Output ''
Write-Output 'DONE'
