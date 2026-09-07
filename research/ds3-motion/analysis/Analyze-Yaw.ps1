# Yaw gyro sign and scale from the probe's --interactive streams.
#
# Reproduces docs/MOTION.md, section "Yaw: sign and scale": the raw yaw value FALLS for a
# clockwise turn seen from above (first turn integrates negative on every pad), and the
# scale is ~1.4 counts per (deg/s) - eight turns, median 1.43, mean 1.45 +- 7 % excluding
# one short return turn - i.e. ~0.7 deg/s per count, roughly +-360 deg/s full scale.
# The residual error is the hand-estimated 90 deg per turn, not the statistics.
#
# Method:
#  * phase blocks split on >150 ms gaps in t_ms; six 2.5 s orientation blocks then the 5 s yaw block
#  * gyro zero taken from the ORIENTATION blocks (pad demonstrably stationary there),
#    not from the yaw window (the pad is turning for most of it on some runs)
#  * turns = maximal runs of constant-sign deviation (|dev|<=2 counts is neutral and
#    attaches to the current run), so back-to-back turns without a pause still split
#  * user's motion: pad flat, ~90 deg clockwise seen from above, pause, ~90 deg back, repeated
#
# Usage: powershell -ExecutionPolicy Bypass -File .\Analyze-Yaw.ps1   (reads ../dumps/*_stream.csv)
$ErrorActionPreference = 'Stop'
$dumps = Join-Path $PSScriptRoot '..\dumps'
$all = New-Object System.Collections.ArrayList

function Analyze([string]$name) {
    $csv = Join-Path $dumps "$name`_stream.csv"
    if (-not (Test-Path $csv)) { return }
    $rows = @(Import-Csv $csv)
    if ($rows.Count -lt 200) { Write-Host "`n### $name : $($rows.Count) rows, skipped"; return }
    $t = [double[]]($rows | ForEach-Object { [double]$_.t_ms })
    $g = [double[]]($rows | ForEach-Object { [double]$_.rawG })

    $blocks = New-Object System.Collections.ArrayList
    $st = 0
    for ($i = 1; $i -lt $t.Count; $i++) { if (($t[$i] - $t[$i - 1]) -gt 150) { [void]$blocks.Add(@($st, ($i - 1))); $st = $i } }
    [void]$blocks.Add(@($st, ($t.Count - 1)))

    # zero from the stationary orientation blocks (200-260 samples each)
    $still = @()
    foreach ($b in $blocks) { $c = $b[1] - $b[0] + 1; if ($c -ge 150 -and $c -lt 450) { for ($i = $b[0]; $i -le $b[1]; $i++) { $still += $g[$i] } } }
    if ($still.Count -lt 200) { Write-Host "`n### $name : no stationary blocks"; return }
    $zero = ($still | Measure-Object -Average).Average
    $acc = 0.0; foreach ($v in $still) { $acc += ($v - $zero) * ($v - $zero) }
    $zsd = [math]::Sqrt($acc / $still.Count)

    $yaw = $null
    foreach ($b in $blocks) { if (($b[1] - $b[0] + 1) -ge 450) { $yaw = $b; break } }
    if (-not $yaw) { Write-Host "`n### $name : no 5 s yaw block"; return }
    $a = $yaw[0]; $b = [math]::Min($yaw[1], $a + 502)

    Write-Host ("`n### {0}" -f $name)
    Write-Host ("    gyro zero from {0} stationary samples: {1:F2} (sd {2:F2})   yaw window t={3:F0}..{4:F0} ms n={5}" -f $still.Count, $zero, $zsd, $t[$a], $t[$b], ($b - $a + 1))

    # sign runs
    $runs = New-Object System.Collections.ArrayList
    $curSign = 0; $s0 = $a
    for ($i = $a; $i -le $b; $i++) {
        $d = $g[$i] - $zero
        $sg = if ([math]::Abs($d) -le 2) { 0 } else { [math]::Sign($d) }
        if ($sg -eq 0) { continue }
        if ($curSign -eq 0) { $curSign = $sg; $s0 = $i }
        elseif ($sg -ne $curSign) { [void]$runs.Add(@($s0, ($i - 1), $curSign)); $curSign = $sg; $s0 = $i }
    }
    if ($curSign -ne 0) { [void]$runs.Add(@($s0, $b, $curSign)) }

    Write-Host "    turn   t_ms   dur_ms    peak   integral(counts*s)   |int|/90deg   dir"
    $k = 1
    foreach ($r in $runs) {
        $x0 = $r[0]; $x1 = $r[1]
        $integral = 0.0; $peak = 0.0
        for ($x = $x0; $x -le $x1; $x++) {
            $dev = $g[$x] - $zero
            $dt = if ($x -lt $x1) { ($t[$x + 1] - $t[$x]) / 1000.0 } else { 0.01 }
            $integral += $dev * $dt
            if ([math]::Abs($dev) -gt [math]::Abs($peak)) { $peak = $dev }
        }
        if ([math]::Abs($integral) -lt 30) { continue }   # ignore noise / tiny wobbles
        $per90 = [math]::Abs($integral) / 90
        $dir = if ($k -eq 1) { 'CW (1st)' } elseif ($k % 2 -eq 0) { 'CCW' } else { 'CW' }
        Write-Host ("    {0,4} {1,6:F0} {2,8:F0} {3,7:+0;-0} {4,20:+0.0;-0.0} {5,13:F3}   {6}" -f `
            $k, $t[$x0], ($t[$x1] - $t[$x0]), $peak, $integral, $per90, $dir)
        [void]$all.Add([pscustomobject]@{ pad = ($name -replace '_2026.*', ''); turn = $k; sign = [math]::Sign($integral); per90 = $per90 })
        $k++
    }
}

Write-Host "User's motion: pad flat on the desk, ~90 deg CLOCKWISE seen from above first,"
Write-Host "pause, ~90 deg back (CCW), repeated 2-3 times."
foreach ($nm in 'ds3-cechzc2e-a1_20260906-171710', 'ds3-cechzc2u-a2_20260906-171948',
    'ds3-cechzc2e-a1_20260906-172132', 'ds3-cechzc2e_20260906-172301', 'ds3_20260906-161449') { Analyze $nm }

Write-Host "`n=================== SUMMARY ==================="
$all | Format-Table -AutoSize
$vals = @($all | ForEach-Object { $_.per90 })
if ($vals.Count -gt 1) {
    $m = ($vals | Measure-Object -Average).Average
    $acc = 0.0; foreach ($v in $vals) { $acc += ($v - $m) * ($v - $m) }
    $sd = [math]::Sqrt($acc / ($vals.Count - 1))
    Write-Host ("turns {0}   mean {1:F3} counts/(deg/s)   sd {2:F3} ({3:F0} %)   range {4:F3}..{5:F3}" -f `
        $vals.Count, $m, $sd, (100 * $sd / $m), ($vals | Measure-Object -Minimum).Minimum, ($vals | Measure-Object -Maximum).Maximum)
    Write-Host ("=> {0:F2} deg/s per count; +-511 counts ~ +-{1:F0} deg/s full scale" -f (1 / $m), (511 / $m))
    Write-Host "`nsign of the FIRST (clockwise) turn on each pad:"
    $all | Group-Object pad | ForEach-Object { $f = ($_.Group | Sort-Object turn)[0]; Write-Host ("  {0,-32} {1,+2}" -f $_.Name, $f.sign) }
    Write-Host "`nalternation check (should flip every turn):"
    $all | Group-Object pad | ForEach-Object { Write-Host ("  {0,-32} {1}" -f $_.Name, (($_.Group | Sort-Object turn | ForEach-Object { if ($_.sign -lt 0) { '-' } else { '+' } }) -join ' ')) }
}
