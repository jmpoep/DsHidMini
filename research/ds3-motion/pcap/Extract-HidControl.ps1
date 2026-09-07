# Extracts HID class control transfers (setup + data stage) from USB link-layer pcaps.
# Usage: .\Extract-HidControl.ps1 -Pcap <file> [-Out <file>]
param(
    [Parameter(Mandatory = $true)][string]$Pcap,
    [string]$Out = ""
)

$ts = 'C:\Program Files\Wireshark\tshark.exe'
if (-not (Test-Path -LiteralPath $ts)) {
    throw "tshark not found at '$ts'"
}

$tsharkOut = & $ts -r $Pcap -Y "usbll.data" -T fields -e frame.number -e frame.time_relative -e usbll.pid -e usbll.src -e usbll.dst -e usbll.data
if ($LASTEXITCODE -ne 0) {
    throw "tshark failed with exit code $LASTEXITCODE while extracting HID control transfers from '$Pcap'"
}

$rows = @($tsharkOut | ForEach-Object {
        $p = $_ -split "`t"
        [pscustomobject]@{ Frame = [int]$p[0]; Time = [double]$p[1]; Pid = $p[2]; Src = $p[3]; Dst = $p[4]; Data = $p[5] }
    })

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# $Pcap")
[void]$sb.AppendLine("# frame  time        setup             dir  payload")

for ($i = 0; $i -lt $rows.Count; $i++) {
    $r = $rows[$i]
    if ($r.Src -ne 'host' -or $r.Dst -notmatch '\.0$' -or $r.Data.Length -ne 16) { continue }
    if ($r.Data -notmatch '^(21|a1)(01|09|0a|0b)') { continue }

    $bmReq = [Convert]::ToInt32($r.Data.Substring(0, 2), 16)
    $bReq = $r.Data.Substring(2, 2)
    $wValue = $r.Data.Substring(6, 2) + $r.Data.Substring(4, 2)
    $wLength = [Convert]::ToInt32($r.Data.Substring(14, 2) + $r.Data.Substring(12, 2), 16)
    $devToHost = ($bmReq -band 0x80) -ne 0
    $dev = $r.Dst

    # collect data stage
    $payload = ''
    $j = $i + 1
    while ($j -lt $rows.Count) {
        $n = $rows[$j]
        # stop at next setup packet to EP0
        if ($n.Src -eq 'host' -and $n.Dst -eq $dev -and $n.Data -match '^(21|a1|80|00|c0|40)' -and $n.Data.Length -eq 16 -and $payload.Length -ge ($wLength * 2)) { break }
        if ($devToHost) {
            if ($n.Src -eq $dev -and $n.Dst -eq 'host') {
                if ($n.Data.Length -eq 0) { break } # ZLP => end
                $payload += $n.Data
                if ($payload.Length -ge ($wLength * 2)) { break }
            }
            elseif ($n.Src -eq 'host' -and $n.Dst -eq $dev -and $n.Data.Length -eq 0) { break } # status stage
        }
        else {
            if ($n.Src -eq 'host' -and $n.Dst -eq $dev -and $n.Data.Length -gt 0 -and $n.Data.Length -ne 16) {
                $payload += $n.Data
            }
            elseif ($n.Src -eq 'host' -and $n.Dst -eq $dev -and $n.Data.Length -eq 16 -and $payload.Length -lt ($wLength * 2)) {
                # 8-byte OUT data has the same length as SETUP; identify SETUP by usbll.pid (tshark: SETUP / 0x2d)
                if ($n.Pid -match '^(SETUP|0x2[dD]|2[dD]|45)$') { break }
                $payload += $n.Data
            }
            elseif ($n.Src -eq $dev -and $n.Dst -eq 'host' -and $n.Data.Length -eq 0) { break }
            if ($payload.Length -ge ($wLength * 2)) { break }
        }
        $j++
    }

    $dir = if ($devToHost) { 'IN ' } else { 'OUT' }
    $reqName = switch ($bReq) { '01' { 'GET_REPORT' } '09' { 'SET_REPORT' } '0a' { 'SET_IDLE' } '0b' { 'SET_PROTOCOL' } default { $bReq } }
    $type = switch ($wValue.Substring(0, 2)) { '01' { 'Input' } '02' { 'Output' } '03' { 'Feature' } default { '?' } }
    $line = '{0,6}  {1,10:F6}  {2,-12} {3} 0x{4} len={5,-3} {6}  {7}' -f $r.Frame, $r.Time, $reqName, $type, $wValue.Substring(2, 2).ToUpper(), $wLength, $dir, ($payload -replace '(..)', '$1 ').Trim()
    [void]$sb.AppendLine($line)
}

if ($Out) { $sb.ToString() | Out-File -Encoding utf8 $Out } else { $sb.ToString() }
