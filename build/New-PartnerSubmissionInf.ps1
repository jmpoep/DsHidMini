#Requires -Version 5.1
<#
.SYNOPSIS
    Expands a stampinf-processed x64 dshidmini.inf into a dual-arch Partner Center INF.

.DESCRIPTION
    Partner Center requires every driver folder in a CAB to declare every requested
    architecture. This takes the already-stamped x64 INF (DriverVer and UMDF version
    filled) and emits NTARM64 model sections plus SourceDisksFiles.amd64/.arm64 so
    one package folder can carry both DLLs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InputInf,

    [Parameter(Mandatory)]
    [string] $OutputInf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$InputInf = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($InputInf)
$OutputInf = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($OutputInf)

if (-not (Test-Path -LiteralPath $InputInf)) {
    throw "Input INF not found: $InputInf"
}

$text = [System.IO.File]::ReadAllText($InputInf)
if ([string]::IsNullOrWhiteSpace($text)) {
    throw "Input INF is empty: $InputInf"
}

if ($text -match '\$ARCH\$' -or $text -match 'NT\$ARCH\$' -or $text -match '\$UMDFVERSION\$') {
    throw "Input INF is not stampinf-processed (unexpanded `$ARCH`$ or `$UMDFVERSION`$ remain): $InputInf"
}

$newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }

$headerMatches = [regex]::Matches($text, '(?m)^(\[[^\]]+\])\r?\n')
if ($headerMatches.Count -eq 0) {
    throw "No INF sections found in $InputInf"
}

$preamble = $text.Substring(0, $headerMatches[0].Index)
$sections = [System.Collections.Generic.List[object]]::new()
for ($i = 0; $i -lt $headerMatches.Count; $i++) {
    $header = $headerMatches[$i].Groups[1].Value
    $bodyStart = $headerMatches[$i].Index + $headerMatches[$i].Length
    $bodyEnd = if ($i + 1 -lt $headerMatches.Count) { $headerMatches[$i + 1].Index } else { $text.Length }
    $sections.Add([pscustomobject]@{
            Header = $header
            Body   = $text.Substring($bodyStart, $bodyEnd - $bodyStart)
        })
}

function ConvertTo-Arm64Decoration([string] $Value) {
    return (($Value -creplace 'NTAMD64', 'NTARM64') -creplace 'NTamd64', 'NTarm64')
}

$outSections = [System.Collections.Generic.List[object]]::new()
$clonedModels = 0
$replacedSourceDisks = $false

foreach ($section in $sections) {
    if ($section.Header -eq '[Manufacturer]') {
        if ($section.Body -notmatch 'NTAMD64' -and $section.Body -notmatch 'NTamd64') {
            throw "[Manufacturer] does not contain NTAMD64 decorations; expected a stamped x64 INF."
        }
        if ($section.Body -notmatch 'NTARM64' -and $section.Body -notmatch 'NTarm64') {
            $section.Body = [regex]::Replace($section.Body, '(?m)^(.+=Nefarius,\s*)(.+)$', {
                    param($match)
                    $decorations = $match.Groups[2].Value.TrimEnd()
                    $armDecorations = ConvertTo-Arm64Decoration $decorations
                    $match.Groups[1].Value + $decorations + ', ' + $armDecorations
                })
        }
        $outSections.Add($section)
        continue
    }

    if ($section.Header -eq '[SourceDisksFiles]') {
        if ($section.Body -notmatch '(?im)^dshidmini\.dll=1\s*$') {
            throw "[SourceDisksFiles] does not contain the expected 'dshidmini.dll=1' entry."
        }
        $amdBody = [regex]::Replace($section.Body, '(?im)^(dshidmini\.dll=)1(\s*)$', '${1}1,x64$2')
        $armBody = [regex]::Replace($section.Body, '(?im)^(dshidmini\.dll=)1(\s*)$', '${1}1,ARM64$2')
        $outSections.Add([pscustomobject]@{ Header = '[SourceDisksFiles.amd64]'; Body = $amdBody })
        $outSections.Add([pscustomobject]@{ Header = '[SourceDisksFiles.arm64]'; Body = $armBody })
        $replacedSourceDisks = $true
        continue
    }

    if ($section.Header -match '^\[SourceDisksFiles\.') {
        throw "Input INF already has decorated $($section.Header); expected undecorated [SourceDisksFiles] from a per-arch WDK build."
    }

    $outSections.Add($section)

    if ($section.Header -match '^\[Nefarius\.NTAMD64\.' -or $section.Header -match '^\[Nefarius\.NTamd64\.') {
        $armHeader = ConvertTo-Arm64Decoration $section.Header
        $outSections.Add([pscustomobject]@{ Header = $armHeader; Body = $section.Body })
        $clonedModels++
    }
}

if (-not $replacedSourceDisks) {
    throw "Input INF is missing [SourceDisksFiles]."
}
if ($clonedModels -lt 2) {
    throw "Expected to clone at least two NTAMD64 model sections; cloned $clonedModels."
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.Append($preamble)
foreach ($section in $outSections) {
    [void]$builder.Append($section.Header)
    [void]$builder.Append($newline)
    [void]$builder.Append($section.Body)
}

$result = $builder.ToString()

$required = @(
    '[Nefarius.NTAMD64.10.0...17763]',
    '[Nefarius.NTAMD64.10.0...22000]',
    '[Nefarius.NTARM64.10.0...17763]',
    '[Nefarius.NTARM64.10.0...22000]',
    '[SourceDisksFiles.amd64]',
    '[SourceDisksFiles.arm64]'
)
foreach ($header in $required) {
    if (-not [regex]::IsMatch($result, '(?im)^' + [regex]::Escape($header) + '\r?$')) {
        throw "Generated INF is missing required section $header"
    }
}
if ([regex]::IsMatch($result, '(?m)^\[SourceDisksFiles\]\s*$')) {
    throw "Generated INF still has undecorated [SourceDisksFiles]."
}
if ($result -match '\$ARCH\$' -or $result -match '\$UMDFVERSION\$') {
    throw "Generated INF still contains unexpanded tokens."
}

$outputDir = Split-Path -Parent $OutputInf
if ($outputDir -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($OutputInf, $result, $utf8)
Write-Output "Wrote dual-arch partner INF: $OutputInf"
