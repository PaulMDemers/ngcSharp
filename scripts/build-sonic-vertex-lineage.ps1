param(
    [Parameter(Mandatory = $true)]
    [string]$ProvenanceCsvPath,
    [Parameter(Mandatory = $true)]
    [string[]]$WriteTraceCsvPath,
    [string[]]$TransformCsvPath = @(),
    [string]$LineageCsvPath = "",
    [string]$SummaryJsonPath = "",
    [int]$RecordSize = 0x20,
    [int]$Top = 12
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Parse-HexOrDecimal {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return [uint32]0
    }

    $trimmed = $Text.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

function Test-RangeOverlap {
    param(
        [uint32]$Address,
        [uint64]$Length,
        [uint32]$TargetAddress,
        [uint64]$TargetLength
    )

    if ($Length -eq 0 -or $TargetLength -eq 0) {
        return $false
    }

    $end = [uint64]$Address + $Length
    $targetEnd = [uint64]$TargetAddress + $TargetLength
    return ([uint64]$Address -lt $targetEnd) -and ([uint64]$TargetAddress -lt $end)
}

function Get-RecordBase {
    param(
        [uint32]$Address,
        [uint32]$VertexBase,
        [int]$Size
    )

    if ($Address -lt $VertexBase) {
        return [uint32]0
    }

    $offset = [uint32]($Address - $VertexBase)
    return [uint32]($VertexBase + ($offset - ($offset % [uint32]$Size)))
}

function Get-LastOverlappingWrite {
    param(
        [object[]]$Writes,
        [uint32]$RecordAddress,
        [int]$Size,
        [int64]$BeforeInstruction
    )

    $best = $null
    foreach ($write in $Writes) {
        if ([int64]$write._instruction -gt $BeforeInstruction) {
            continue
        }

        if (-not (Test-RangeOverlap $write._address ([uint64]$write._width) $RecordAddress ([uint64]$Size))) {
            continue
        }

        if ($null -eq $best -or [int64]$write._instruction -gt [int64]$best._instruction) {
            $best = $write
        }
    }

    return $best
}

function Get-TransformForRecord {
    param(
        [object[]]$Transforms,
        [uint32]$RecordAddress,
        [int64]$BeforeInstruction
    )

    if ($Transforms.Count -eq 0) {
        return $null
    }

    $best = $null
    foreach ($transform in $Transforms) {
        if ([int64]$transform._instruction -gt $BeforeInstruction) {
            continue
        }

        if (-not (Test-RangeOverlap $transform._outputCursor $transform._outputSpan $RecordAddress 0x20)) {
            continue
        }

        if ($null -eq $best -or [int64]$transform._instruction -gt [int64]$best._instruction) {
            $best = $transform
        }
    }

    return $best
}

function Add-ToBucket {
    param(
        [hashtable]$Buckets,
        [string]$Key,
        [object]$Value
    )

    if (-not $Buckets.ContainsKey($Key)) {
        $Buckets[$Key] = [System.Collections.Generic.List[object]]::new()
    }

    $Buckets[$Key].Add($Value)
}

$provenancePath = Resolve-FullPath $ProvenanceCsvPath
$writeTracePaths = @($WriteTraceCsvPath | ForEach-Object { Resolve-FullPath $_ })
if (-not (Test-Path -LiteralPath $provenancePath)) {
    throw "Provenance CSV not found: $provenancePath"
}

foreach ($writeTracePath in $writeTracePaths) {
    if (-not (Test-Path -LiteralPath $writeTracePath)) {
        throw "Write trace CSV not found: $writeTracePath"
    }
}

$baseDirectory = Split-Path -Parent $provenancePath
if ([string]::IsNullOrWhiteSpace($LineageCsvPath)) {
    $LineageCsvPath = Join-Path $baseDirectory "sonic-vertex-lineage.csv"
} else {
    $LineageCsvPath = Resolve-FullPath $LineageCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $baseDirectory "sonic-vertex-lineage.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$provenanceRows = @(Import-Csv -LiteralPath $provenancePath)
$writeRows = @($writeTracePaths | ForEach-Object { Import-Csv -LiteralPath $_ })
if ($provenanceRows.Count -eq 0) {
    throw "Provenance CSV has no rows: $provenancePath"
}

if ($writeRows.Count -eq 0) {
    throw "Write trace CSV has no rows."
}

$candidateRecordAddresses = @($provenanceRows | ForEach-Object { Parse-HexOrDecimal $_.source_record } | Sort-Object -Unique)

foreach ($row in $writeRows) {
    $row | Add-Member -NotePropertyName _instruction -NotePropertyValue ([int64]$row.instruction)
    $row | Add-Member -NotePropertyName _address -NotePropertyValue (Parse-HexOrDecimal $row.address)
    $row | Add-Member -NotePropertyName _width -NotePropertyValue ([Math]::Max(1, [int]$row.width))
}

$writeBuckets = @{}
foreach ($write in $writeRows) {
    foreach ($recordAddress in $candidateRecordAddresses) {
        if (Test-RangeOverlap $write._address ([uint64]$write._width) $recordAddress ([uint64]$RecordSize)) {
            Add-ToBucket $writeBuckets (Format-Hex32 $recordAddress) $write
        }
    }
}

$transformRows = @()
$transformPaths = @($TransformCsvPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { Resolve-FullPath $_ })
foreach ($transformPath in $transformPaths) {
    if (-not (Test-Path -LiteralPath $transformPath)) {
        throw "Transform CSV not found: $transformPath"
    }

    $transformRows += @(Import-Csv -LiteralPath $transformPath)
}

if ($transformRows.Count -ne 0) {
    foreach ($row in $transformRows) {
        $outputCursor = Parse-HexOrDecimal $row.output_cursor
        $iterations = Parse-HexOrDecimal $row.iterations
        $span = [uint64](([Math]::Max(1, [int]$iterations) * $RecordSize) + $RecordSize)
        $row | Add-Member -NotePropertyName _instruction -NotePropertyValue ([int64]$row.instruction)
        $row | Add-Member -NotePropertyName _outputCursor -NotePropertyValue $outputCursor
        $row | Add-Member -NotePropertyName _outputSpan -NotePropertyValue $span
    }
}

$transformBuckets = @{}
foreach ($transform in $transformRows) {
    foreach ($recordAddress in $candidateRecordAddresses) {
        if (Test-RangeOverlap $transform._outputCursor $transform._outputSpan $recordAddress ([uint64]$RecordSize)) {
            Add-ToBucket $transformBuckets (Format-Hex32 $recordAddress) $transform
        }
    }
}

$lineage = foreach ($row in $provenanceRows) {
    $instruction = [int64]$row.instruction
    $sourceRecord = Parse-HexOrDecimal $row.source_record
    $vertexBase = Parse-HexOrDecimal $row.vertex_base
    $recordBase = Get-RecordBase $sourceRecord $vertexBase $RecordSize
    $writeCandidates = if ($writeBuckets.ContainsKey($row.source_record)) { @($writeBuckets[$row.source_record]) } else { @() }
    $lastWrite = Get-LastOverlappingWrite $writeCandidates $sourceRecord $RecordSize $instruction
    $lastRecordWrite = $lastWrite
    $transformCandidates = if ($transformBuckets.ContainsKey($row.source_record)) { @($transformBuckets[$row.source_record]) } else { @() }
    $transform = Get-TransformForRecord $transformCandidates $sourceRecord $instruction

    [pscustomobject][ordered]@{
        instruction = $instruction
        gx_fifo_offset = $row.gx_fifo_offset
        packet = $row.packet
        stream_record = $row.stream_record
        stream_offset = $row.stream_offset
        record_bytes = $row.record_bytes
        decoded_index = $row.decoded_index
        source_record = $row.source_record
        record_base = Format-Hex32 $recordBase
        source_x = $row.source_x
        source_y = $row.source_y
        source_z = $row.source_z
        source_color = $row.source_color
        source_x_float = $row.source_x_float
        source_y_float = $row.source_y_float
        source_z_float = $row.source_z_float
        write_instruction = if ($lastRecordWrite) { $lastRecordWrite.instruction } else { "" }
        write_pc = if ($lastRecordWrite) { $lastRecordWrite.pc } else { "" }
        write_disassembly = if ($lastRecordWrite) { $lastRecordWrite.disassembly } else { "" }
        write_kind = if ($lastRecordWrite) { $lastRecordWrite.kind } else { "" }
        write_address = if ($lastWrite) { $lastWrite.address } else { "" }
        write_value = if ($lastWrite) { $lastWrite.value } else { "" }
        write_range_bytes = if ($lastRecordWrite) { $lastRecordWrite.range_bytes } else { "" }
        transform_instruction = if ($transform) { $transform.instruction } else { "" }
        transform_pc = if ($transform) { $transform.pc } else { "" }
        transform_output_cursor = if ($transform) { $transform.output_cursor } else { "" }
        transform_input_cursor = if ($transform) { $transform.input_cursor } else { "" }
        transform_iterations = if ($transform) { $transform.iterations } else { "" }
        transform_gqr1 = if ($transform) { $transform.gqr1 } else { "" }
        transform_input_bytes = if ($transform) { $transform.input_bytes } else { "" }
        transform_output_bytes = if ($transform) { $transform.output_bytes } else { "" }
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LineageCsvPath) | Out-Null
$lineage | Export-Csv -LiteralPath $LineageCsvPath -NoTypeInformation

$summary = [pscustomobject][ordered]@{
    provenanceCsvPath = $provenancePath
    writeTraceCsvPaths = @($writeTracePaths)
    transformCsvPaths = @($transformPaths)
    lineageCsvPath = $LineageCsvPath
    rowCount = $lineage.Count
    rowsWithWrites = @($lineage | Where-Object { -not [string]::IsNullOrWhiteSpace($_.write_instruction) }).Count
    rowsWithTransforms = @($lineage | Where-Object { -not [string]::IsNullOrWhiteSpace($_.transform_instruction) }).Count
    writePcs = @($lineage | Where-Object { $_.write_pc } | Group-Object write_pc | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    transformPcs = @($lineage | Where-Object { $_.transform_pc } | Group-Object transform_pc | Sort-Object Count -Descending | Select-Object -First $Top Name, Count)
    firstRows = @($lineage | Select-Object -First $Top instruction, gx_fifo_offset, packet, source_record, write_instruction, write_pc, write_address, transform_instruction, transform_pc, transform_output_cursor, transform_input_cursor)
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Sonic vertex lineage: $LineageCsvPath"
$lineage | Select-Object -First $Top instruction,gx_fifo_offset,packet,source_record,write_instruction,write_pc,write_address,transform_instruction,transform_pc,transform_output_cursor,transform_input_cursor | Format-Table -AutoSize
