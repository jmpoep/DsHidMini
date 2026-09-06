# Reproduces docs/MOTION.md accelerometer formula check (126 values).
# Throwaway: re-derive every "cal X/Y/Z" value in the probe logs from the raw averages
# using the sixaxis.sys accelerometer formula, and report mismatches.
$ErrorActionPreference = 'Stop'
$dumps = Join-Path $PSScriptRoot '..\dumps'

function Cal([int]$raw, [int]$zero, [int]$oneG) {
    if ($zero -eq $oneG) { return $raw }
    # C integer semantics: truncation toward zero at both divisions
    $a = [int][math]::Truncate([double](($raw - $zero) * 1024) / ($zero - $oneG))
    $b = [int][math]::Truncate([double]($a * 113) / 1024)
    return $b + 512
}

$total = 0; $bad = 0
foreach ($f in Get-ChildItem $dumps -Filter '*.txt' | Where-Object { $_.Name -match '^(ds3|fake|obigben|clone|sixaxis)' }) {
    $txt = Get-Content -Encoding UTF8 $f.FullName
    $calLine = $txt | Select-String 'page 0xA0 calibration \(BE u16 at 0x11\): X (\d+)/(\d+) .*Y (\d+)/(\d+) .*Z (\d+)/(\d+) .*G (\d+)/(\d+)' | Select-Object -First 1
    if (-not $calLine) { continue }
    $m = $calLine.Matches[0].Groups
    $zx = [int]$m[1].Value; $ox = [int]$m[2].Value; $zy = [int]$m[3].Value; $oy = [int]$m[4].Value; $zz = [int]$m[5].Value; $oz = [int]$m[6].Value
    Write-Host ("`n### {0}  X {1}/{2} Y {3}/{4} Z {5}/{6}" -f $f.BaseName, $zx, $ox, $zy, $oy, $zz, $oz)
    foreach ($l in ($txt | Select-String 'n=(\d+) raw avg X ([\d.]+) Y ([\d.]+) Z ([\d.]+) G ([\d.]+) \| cal X (-?\d+) Y (-?\d+) Z (-?\d+)')) {
        $g = $l.Matches[0].Groups
        if ([int]$g[1].Value -eq 0) { continue }
        $rx = [int][math]::Round([double]$g[2].Value); $ry = [int][math]::Round([double]$g[3].Value); $rz = [int][math]::Round([double]$g[4].Value)
        $ex = Cal $rx $zx $ox; $ey = Cal $ry $zy $oy; $ez = Cal $rz $zz $oz
        $lx = [int]$g[6].Value; $ly = [int]$g[7].Value; $lz = [int]$g[8].Value
        $ok = ($ex -eq $lx) -and ($ey -eq $ly) -and ($ez -eq $lz)
        $total += 3; if (-not $ok) { $bad++ }
        $pose = ($l.Line -replace '^\s+', '') -replace ':.*$', ''
        Write-Host ("  {0,-70} raw {1,3} {2,3} {3,3} -> formula {4,3} {5,3} {6,3} | log {7,3} {8,3} {9,3} {10}" -f $pose.Substring(0, [math]::Min(70, $pose.Length)), $rx, $ry, $rz, $ex, $ey, $ez, $lx, $ly, $lz, $(if ($ok) { 'OK' } else { 'MISMATCH' }))
    }
}
Write-Host "`nchecked $total values, $bad mismatching poses"

# 1g magnitude check per genuine pad: |cal - 512| at +/-1g should be ~113
Write-Host "`n### +/-1g response (cal - 512) per pad, from the six poses"
foreach ($f in Get-ChildItem $dumps -Filter '*.txt' | Where-Object { $_.Name -match '^(ds3|obigben|fake)' }) {
    $txt = Get-Content -Encoding UTF8 $f.FullName
    $vals = @{}
    foreach ($l in ($txt | Select-String '^\s+(flat on table|upside down|standing on its left grip|standing on its right grip|front edge down|back edge down).*\| cal X (-?\d+) Y (-?\d+) Z (-?\d+)')) {
        $g = $l.Matches[0].Groups; $vals[$g[1].Value] = @([int]$g[2].Value, [int]$g[3].Value, [int]$g[4].Value)
    }
    if ($vals.Count -lt 6) { continue }
    Write-Host ("  {0,-45} Z: flat {1,4:+0;-0} / upside {2,4:+0;-0}   X: Lgrip {3,4:+0;-0} / Rgrip {4,4:+0;-0}   Y: front {5,4:+0;-0} / back {6,4:+0;-0}" -f $f.BaseName,
        ($vals['flat on table'][2] - 512), ($vals['upside down'][2] - 512),
        ($vals['standing on its left grip'][0] - 512), ($vals['standing on its right grip'][0] - 512),
        ($vals['front edge down'][1] - 512), ($vals['back edge down'][1] - 512))
}
