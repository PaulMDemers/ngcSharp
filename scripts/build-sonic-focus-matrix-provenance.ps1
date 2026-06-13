param(
    [Parameter(Mandatory = $true)]
    [string]$MatrixWriterSummaryCsvPath,
    [string]$FocusPacket = "0x813184D0",
    [int]$ContextRows = 12,
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-HexUInt32 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        $text = $text.Substring(2)
    }

    return [uint32]::Parse($text, [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-Int64 {
    param([string]$Value)

    return [int64]::Parse($Value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Hex32 {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return "0x{0:X8}" -f [uint32]$Value
}

function Get-StoreOffset {
    param(
        [uint32]$StoreAddress,
        [uint32]$BaseAddress
    )

    return "0x{0:X}" -f [uint32]($StoreAddress - $BaseAddress)
}

function Test-AddressInRange {
    param(
        $Address,
        $Start,
        [uint32]$Length
    )

    if ($null -eq $Address -or $null -eq $Start) {
        return $false
    }

    $address64 = [uint64][uint32]$Address
    $start64 = [uint64][uint32]$Start
    return $address64 -ge $start64 -and $address64 -lt ($start64 + [uint64]$Length)
}

function New-ProvenanceRow {
    param(
        [string]$Role,
        [object]$FocusRow,
        [object]$Row,
        $SourceMatrix,
        $OutputMatrix
    )

    $focusInstruction = Convert-Int64 $FocusRow.instruction
    $instruction = Convert-Int64 $Row.instruction
    $storeAddress = Convert-HexUInt32 $Row.store_address
    $storeOffset = if (Test-AddressInRange $storeAddress $SourceMatrix 0x30) {
        Get-StoreOffset $storeAddress $SourceMatrix
    } elseif (Test-AddressInRange $storeAddress $OutputMatrix 0x30) {
        Get-StoreOffset $storeAddress $OutputMatrix
    } else {
        ""
    }

    [pscustomobject][ordered]@{
        role = $Role
        focus_instruction = $focusInstruction
        instruction = $instruction
        instruction_delta = $instruction - $focusInstruction
        pc = $Row.pc
        lr = $Row.lr
        store_address = $Row.store_address
        store_offset = $storeOffset
        source_matrix = Format-Hex32 $SourceMatrix
        output_matrix = Format-Hex32 $OutputMatrix
        packet = $Row.packet
        r3 = $Row.r3
        r4 = $Row.r4
        r5 = $Row.r5
        r6 = $Row.r6
        packet_stream0 = $Row.packet_stream0
        packet_stream1 = $Row.packet_stream1
        source_translation = $Row.source_translation
        packed_translation = $Row.packed_translation
        packed_col0 = $Row.packed_col0
        packed_col1 = $Row.packed_col1
        packed_col2 = $Row.packed_col2
        disassembly = $Row.disassembly
    }
}

$matrixWriterPath = Resolve-FullPath $MatrixWriterSummaryCsvPath
if (-not (Test-Path -LiteralPath $matrixWriterPath)) {
    throw "Sonic matrix-writer summary not found: $matrixWriterPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $matrixWriterPath) "sonic-focus-matrix-provenance"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$rows = @(Import-Csv -LiteralPath $matrixWriterPath | Sort-Object { Convert-Int64 $_.instruction })
if ($rows.Count -eq 0) {
    throw "Sonic matrix-writer summary has no rows: $matrixWriterPath"
}

$focusPacketText = if ($FocusPacket.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
    "0x{0:X8}" -f (Convert-HexUInt32 $FocusPacket)
} else {
    "0x{0:X8}" -f (Convert-HexUInt32 ("0x" + $FocusPacket))
}

$terminalRows = @($rows | Where-Object { $_.packet -eq $focusPacketText -and $_.pc -eq "0x8011C184" })
if ($terminalRows.Count -eq 0) {
    throw "No terminal matrix rows found for focus packet $focusPacketText in $matrixWriterPath"
}

$provenance = New-Object System.Collections.Generic.List[object]
$sourceSummaryRows = New-Object System.Collections.Generic.List[object]

foreach ($terminal in $terminalRows) {
    $focusInstruction = Convert-Int64 $terminal.instruction
    $sourceMatrix = Convert-HexUInt32 $terminal.r5
    $outputMatrix = Convert-HexUInt32 $terminal.r6

    $sourceWrites = @($rows |
        Where-Object {
            (Convert-Int64 $_.instruction) -le $focusInstruction -and
            (Test-AddressInRange (Convert-HexUInt32 $_.store_address) $sourceMatrix 0x30)
        } |
        Sort-Object { Convert-Int64 $_.instruction } |
        Select-Object -Last $ContextRows)

    $outputWrites = @($rows |
        Where-Object {
            [Math]::Abs((Convert-Int64 $_.instruction) - $focusInstruction) -le 16 -and
            (Test-AddressInRange (Convert-HexUInt32 $_.store_address) $outputMatrix 0x30)
        } |
        Sort-Object { Convert-Int64 $_.instruction })

    foreach ($row in $sourceWrites) {
        $provenance.Add((New-ProvenanceRow "source-matrix-write" $terminal $row $sourceMatrix $outputMatrix))
    }

    foreach ($row in $outputWrites) {
        $provenance.Add((New-ProvenanceRow "focus-output-write" $terminal $row $sourceMatrix $outputMatrix))
    }

    $provenance.Add((New-ProvenanceRow "focus-terminal" $terminal $terminal $sourceMatrix $outputMatrix))

    $sourceSummaryRows.Add([pscustomobject][ordered]@{
        focus_packet = $focusPacketText
        focus_instruction = $focusInstruction
        source_matrix = Format-Hex32 $sourceMatrix
        output_matrix = Format-Hex32 $outputMatrix
        source_write_count = $sourceWrites.Count
        source_write_pcs = (($sourceWrites | Select-Object -ExpandProperty pc -Unique) -join " ")
        source_write_lrs = (($sourceWrites | Select-Object -ExpandProperty lr -Unique) -join " ")
        nearest_source_instruction = if ($sourceWrites.Count -gt 0) { ($sourceWrites[-1]).instruction } else { "" }
        nearest_source_delta = if ($sourceWrites.Count -gt 0) { (Convert-Int64 ($sourceWrites[-1]).instruction) - $focusInstruction } else { "" }
        nearest_source_pc = if ($sourceWrites.Count -gt 0) { ($sourceWrites[-1]).pc } else { "" }
        nearest_source_lr = if ($sourceWrites.Count -gt 0) { ($sourceWrites[-1]).lr } else { "" }
        nearest_source_packet = if ($sourceWrites.Count -gt 0) { ($sourceWrites[-1]).packet } else { "" }
        nearest_source_translation = if ($sourceWrites.Count -gt 0) { ($sourceWrites[-1]).source_translation } else { "" }
        focus_source_translation = $terminal.source_translation
        focus_packed_translation = $terminal.packed_translation
    })
}

$provenanceCsv = Join-Path $outputRoot "focus-matrix-provenance.csv"
$sourceSummaryCsv = Join-Path $outputRoot "focus-matrix-source-summary.csv"
$summaryJson = Join-Path $outputRoot "focus-matrix-provenance-summary.json"

$provenance | Export-Csv -LiteralPath $provenanceCsv -NoTypeInformation
$sourceSummaryRows | Export-Csv -LiteralPath $sourceSummaryCsv -NoTypeInformation

$summary = [pscustomobject][ordered]@{
    matrix_writer_summary = $matrixWriterPath
    focus_packet = $focusPacketText
    terminal_rows = $terminalRows.Count
    provenance_rows = $provenance.Count
    provenance_csv = $provenanceCsv
    source_summary_csv = $sourceSummaryCsv
    source_summaries = @($sourceSummaryRows.ToArray())
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryJson
$summary | ConvertTo-Json -Depth 6
