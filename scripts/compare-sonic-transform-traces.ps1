param(
    [Parameter(Mandatory = $true)]
    [string]$FastForwardCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$NoFastForwardCsvPath,
    [Parameter(Mandatory = $true)]
    [uint32]$TargetAddress,
    [string]$CsvPath = "",
    [string]$JsonPath = "",
    [int]$Top = 32,
    [switch]$PassThru
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
        return [uint32]0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToUInt32($text.Substring(2), 16)
    }

    return [Convert]::ToUInt32($text, 16)
}

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

function Get-RowOutputSpan {
    param(
        $Row,
        [bool]$NoFastForward
    )

    if ($NoFastForward) {
        return [uint32]0x20
    }

    $iterations = Convert-HexUInt32 $Row.iterations
    if ($iterations -eq 0) {
        return [uint32]0x20
    }

    $span = [uint64]$iterations * [uint64]0x20
    if ($span -gt [uint32]::MaxValue) {
        return [uint32]::MaxValue
    }

    return [uint32]$span
}

function Row-OverlapsTarget {
    param(
        $Row,
        [uint32]$Span,
        [uint32]$Target
    )

    $start = [uint64](Convert-HexUInt32 $Row.output_cursor)
    $end = $start + [uint64]$Span
    $targetStart = [uint64]$Target
    $targetEnd = $targetStart + [uint64]0x20
    return $start -lt $targetEnd -and $targetStart -lt $end
}

function Get-DerivedInputCursor {
    param(
        $Row,
        [uint32]$Target
    )

    $outputCursor = Convert-HexUInt32 $Row.output_cursor
    $inputCursor = Convert-HexUInt32 $Row.input_cursor
    if ($Target -lt $outputCursor) {
        return $inputCursor
    }

    $recordIndex = [uint32](([uint64]($Target - $outputCursor)) / [uint64]0x20)
    return [uint32]($inputCursor + ($recordIndex * 0x10))
}

function Select-ComparableFields {
    param($Row)

    return [ordered]@{
        pc = $Row.pc
        gqr1 = $Row.gqr1
        f0 = $Row.f0
        f1 = $Row.f1
        f2 = $Row.f2
        f3 = $Row.f3
        f4 = $Row.f4
        f5 = $Row.f5
        f6 = $Row.f6
        f7 = $Row.f7
    }
}

$fastForwardFullPath = Resolve-FullPath $FastForwardCsvPath
$noFastForwardFullPath = Resolve-FullPath $NoFastForwardCsvPath
if (-not (Test-Path -LiteralPath $fastForwardFullPath)) {
    throw "Fast-forward transform CSV not found: $fastForwardFullPath"
}

if (-not (Test-Path -LiteralPath $noFastForwardFullPath)) {
    throw "No-fast-forward transform CSV not found: $noFastForwardFullPath"
}

$fastForwardRows = @(Import-Csv -LiteralPath $fastForwardFullPath)
$noFastForwardRows = @(Import-Csv -LiteralPath $noFastForwardFullPath)

$fastMatches = foreach ($row in $fastForwardRows) {
    $span = Get-RowOutputSpan $row $false
    if (Row-OverlapsTarget $row $span $TargetAddress) {
        [pscustomobject][ordered]@{
            row = $row
            outputSpan = $span
            derivedInputCursor = Get-DerivedInputCursor $row $TargetAddress
        }
    }
}

$noFastMatches = foreach ($row in $noFastForwardRows) {
    $span = Get-RowOutputSpan $row $true
    if (Row-OverlapsTarget $row $span $TargetAddress) {
        [pscustomobject][ordered]@{
            row = $row
            outputSpan = $span
            derivedInputCursor = Convert-HexUInt32 $row.input_cursor
        }
    }
}

$comparisons = foreach ($fast in $fastMatches) {
    $fastFields = Select-ComparableFields $fast.row
    $candidates = @($noFastMatches | Where-Object { $_.derivedInputCursor -eq $fast.derivedInputCursor })
    if ($candidates.Count -eq 0) {
        [pscustomobject][ordered]@{
            target = Format-Hex32 $TargetAddress
            fast_instruction = $fast.row.instruction
            fast_output_cursor = $fast.row.output_cursor
            fast_input_cursor = $fast.row.input_cursor
            derived_input_cursor = Format-Hex32 $fast.derivedInputCursor
            noff_instruction = ""
            noff_output_cursor = ""
            noff_input_cursor = ""
            input_cursor_match = $false
            comparable_fields_match = $false
            first_mismatch = "missing no-fast-forward row"
        }
        continue
    }

    foreach ($candidate in $candidates) {
        $candidateFields = Select-ComparableFields $candidate.row
        $firstMismatch = ""
        foreach ($key in $fastFields.Keys) {
            if ($fastFields[$key] -ne $candidateFields[$key]) {
                $firstMismatch = $key
                break
            }
        }

        [pscustomobject][ordered]@{
            target = Format-Hex32 $TargetAddress
            fast_instruction = $fast.row.instruction
            fast_output_cursor = $fast.row.output_cursor
            fast_input_cursor = $fast.row.input_cursor
            derived_input_cursor = Format-Hex32 $fast.derivedInputCursor
            noff_instruction = $candidate.row.instruction
            noff_output_cursor = $candidate.row.output_cursor
            noff_input_cursor = $candidate.row.input_cursor
            input_cursor_match = $true
            comparable_fields_match = [string]::IsNullOrEmpty($firstMismatch)
            first_mismatch = $firstMismatch
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($CsvPath)) {
    $csvFullPath = Resolve-FullPath $CsvPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $csvFullPath) | Out-Null
    $comparisons | Export-Csv -LiteralPath $csvFullPath -NoTypeInformation
}

$summary = [pscustomobject][ordered]@{
    targetAddress = Format-Hex32 $TargetAddress
    fastForwardCsvPath = $fastForwardFullPath
    noFastForwardCsvPath = $noFastForwardFullPath
    fastForwardMatchCount = @($fastMatches).Count
    noFastForwardMatchCount = @($noFastMatches).Count
    comparisonCount = @($comparisons).Count
    comparableFieldMismatches = @($comparisons | Where-Object { $_.input_cursor_match -and -not $_.comparable_fields_match }).Count
    missingNoFastForwardRows = @($comparisons | Where-Object { -not $_.input_cursor_match }).Count
    sample = @($comparisons | Select-Object -First $Top)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonFullPath
}

if ($PassThru) {
    $summary
} else {
    $summary | ConvertTo-Json -Depth 8
}
