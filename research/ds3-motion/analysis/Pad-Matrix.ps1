# Reproduces docs/MOTION.md pad matrix from probe logs.
# Throwaway: build the per-pad matrix from the probe logs.
$ErrorActionPreference = 'Stop'
$dumps = Join-Path $PSScriptRoot '..\dumps'

$files = 'ds3-cechzc2e-a1_20260906-171710', 'ds3-cechzc2u-a2_20260906-171948',
'ds3-cechzc2e-a1_20260906-172132', 'ds3-cechzc2e_20260906-172301',
'sixaxis_20260906-172427', 'fake-ds3_20260906-172552',
'obigben-aftermarket_20260906-172748', 'sixaxis_20260906-164338',
'ds3_20260906-161449', 'clone_20260906-160146'

function GetBlock([string[]]$lines, [string]$header) {
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -like "$header*") {
            $hex = @()
            for ($j = $i + 1; $j -lt $lines.Count -and $lines[$j] -match '^\s{4}[0-9a-f]{2}( |$)'; $j++) {
                $hex += ($lines[$j].Trim() -split '\s+')
            }
            return $hex
        }
    }
    return $null
}

foreach ($f in $files) {
    $p = Join-Path $dumps "$f.txt"
    if (-not (Test-Path $p)) { continue }
    $l = [IO.File]::ReadAllLines($p, [Text.Encoding]::UTF8)

    $id = GetBlock $l 'GET Feature 0x01'
    $f2 = GetBlock $l 'GET Feature 0xF2'
    $f7 = GetBlock $l 'GET Feature 0xF7'
    if (-not $id) { Write-Host "`n### $f : no identification block"; continue }

    $fw = ($id[2..4] -join ' ')
    $type = ($id[8..11] -join ' ')
    $nf = [Convert]::ToInt32($id[0x25], 16)
    $fields = if ($nf -gt 0) { ($id[0x26..(0x25 + $nf)] -join ' ') } else { '(none)' }
    $has07 = $fields -match '(^| )07( |$)'
    $plainZero = ($nf -ge 2 -and (($id[0x26] -eq '01' -and $id[0x27] -eq '02') -or ($nf -ge 3 -and $id[0x27] -eq '01' -and $id[0x28] -eq '02')))
    $path = if ($has07) { 'HW_CAL  (tracker trims hardware, reports 0x3FF-raw)' }
    elseif ($plainZero) { 'PLAIN_ZERO (software zero vs EEPROM, no tracker)' }
    else { 'SIXAXIS (full software tracker, cal byte at out[3]/[4])' }

    Write-Host ("`n### {0}" -f $f)
    Write-Host ("    fw {0} | type {1} | fields({2}) {3} | 0x07 {4} | path: {5}" -f $fw, $type, $nf, $fields, $has07, $path)
    if ($f2) { Write-Host ("    F2 : {0}   (MAC {1})" -f ($f2[0..17] -join ' '), ($f2[4..9] -join ':')) }
    if ($f7) { Write-Host ("    F7 : {0}" -f ($f7[0..15] -join ' ')) }

    foreach ($pg in '0x00', '0x70', '0x80', '0x90', '0xA0', '0xB0', '0xC0', '0xF0') {
        $blk = GetBlock $l "SET 0xEF page $pg"
        if (-not $blk) { $blk = GetBlock $l "SET 0xEF page $($pg.ToLower())" }
        if (-not $blk) { continue }
        $payload = $blk[0x11..0x20]
        Write-Host ("    {0}: {1}" -f $pg, ($payload -join ' '))
        if ($pg -eq '0xA0') {
            $u = @(); for ($k = 0; $k -lt 16; $k += 2) { $u += [Convert]::ToInt32($payload[$k] + $payload[$k + 1], 16) }
            Write-Host ("          pairs: X {0}/{1} ({2})  Y {3}/{4} ({5})  Z {6}/{7} ({8})  G zero {9} cal 0x{10:X2} ({10})" -f `
                    $u[0], $u[1], ($u[0] - $u[1]), $u[2], $u[3], ($u[2] - $u[3]), $u[4], $u[5], ($u[4] - $u[5]), $u[6], $u[7])
        }
        if ($pg -eq '0x90') {
            $u = @(); for ($k = 0; $k -lt 16; $k += 2) { $u += [Convert]::ToInt32($payload[$k] + $payload[$k + 1], 16) }
            Write-Host ("          stick pairs: ({0},{1}) sum {2} | ({3},{4}) sum {5} | ({6},{7}) sum {8} | ({9},{10}) sum {11}" -f `
                    $u[0], $u[1], ($u[0] + $u[1]), $u[2], $u[3], ($u[2] + $u[3]), $u[4], $u[5], ($u[4] + $u[5]), $u[6], $u[7], ($u[6] + $u[7]))
        }
        if ($pg -eq '0x80' -or $pg -eq '0xB0') {
            $off = if ($pg -eq '0x80') { 7 } else { 0 }
            $a = [Convert]::ToInt32($payload[$off] + $payload[$off + 1], 16); $bb = [Convert]::ToInt32($payload[$off + 2] + $payload[$off + 3], 16)
            Write-Host ("          pair: {0} / {1}" -f $a, $bb)
        }
    }
    foreach ($m in ($l | Select-String 'raw avg X|yaw:|calByte 0x|InitialGyroCal')) { Write-Host ("    | " + $m.Line.Trim()) }
}
