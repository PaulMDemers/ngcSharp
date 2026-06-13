param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$SummaryCsvPath = "",
    [string]$SummaryJsonPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Split-FprPair {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @("", "")
    }

    $parts = $Value -split '\|', 2
    if ($parts.Count -lt 2) {
        return @($parts[0], "")
    }

    return @($parts[0], $parts[1])
}

function Format-VectorText {
    param([string[]]$Values)

    return ($Values -join "/")
}

function Convert-HexBytesToWords {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return @()
    }

    $clean = $Hex.Trim()
    $wordCount = [Math]::Floor($clean.Length / 8)
    $words = New-Object System.Collections.Generic.List[uint32]
    for ($i = 0; $i -lt $wordCount; $i++) {
        $words.Add([uint32]::Parse($clean.Substring($i * 8, 8), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    return $words.ToArray()
}

function Format-Word {
    param($Word)

    if ($null -eq $Word) {
        return ""
    }

    return "0x{0:X8}" -f [uint32]$Word
}

function Get-WordOrNull {
    param(
        [uint32[]]$Words,
        [int]$Index
    )

    if ($Index -lt 0 -or $Index -ge $Words.Length) {
        return $null
    }

    return $Words[$Index]
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Sonic matrix-writer trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "sonic-matrix-writer.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-matrix-writer.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Sonic matrix-writer trace CSV has no rows: $tracePath"
}

$summary = foreach ($row in $rows) {
    $f1 = Split-FprPair $row.f1
    $f3 = Split-FprPair $row.f3
    $f5 = Split-FprPair $row.f5
    $f6 = Split-FprPair $row.f6
    $f7 = Split-FprPair $row.f7
    $f8 = Split-FprPair $row.f8
    $f9 = Split-FprPair $row.f9
    $f10 = Split-FprPair $row.f10
    $f12 = Split-FprPair $row.f12
    $r30Words = @(Convert-HexBytesToWords $row.r30_bytes)

    [pscustomobject]@{
        instruction = [int64]$row.instruction
        pc = $row.pc
        lr = $row.lr
        store_address = $row.store_address
        packet = $row.r30
        r3 = $row.r3
        r4 = $row.r4
        r5 = $row.r5
        r6 = $row.r6
        packet_stream0 = Format-Word (Get-WordOrNull $r30Words 0)
        packet_stream1 = Format-Word (Get-WordOrNull $r30Words 1)
        source_translation = Format-VectorText @($f1[1], $f3[1], $f5[1])
        packed_translation = Format-VectorText @($f9[1], $f10[0], $f10[1])
        packed_col0 = Format-VectorText @($f6[0], $f6[1], $f12[0], $f12[1])
        packed_col1 = Format-VectorText @($f7[0], $f7[1], $f8[0], $f8[1])
        packed_col2 = Format-VectorText @($f9[0], $f9[1], $f10[0], $f10[1])
        disassembly = $row.disassembly
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

$terminalRows = @($summary | Where-Object { $_.pc -eq "0x8011C184" })
Write-Host "Wrote $($summary.Count) matrix-writer rows to $SummaryCsvPath"
Write-Host "Terminal packet rows: $($terminalRows.Count)"
