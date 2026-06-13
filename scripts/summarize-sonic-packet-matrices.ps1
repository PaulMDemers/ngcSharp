param(
    [Parameter(Mandatory = $true)]
    [string]$TimelineCsvPath,
    [string]$CsvPath = "",
    [string]$JsonPath = "",
    [int]$Top = 20,
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

function Convert-HexUInt64 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [uint64]0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToUInt64($text.Substring(2), 16)
    }

    return [Convert]::ToUInt64($text, 16)
}

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

function Format-HexValue {
    param([uint64]$Value)

    return "0x{0:X}" -f $Value
}

function Get-FloatFromBits {
    param([string]$HexValue)

    $bits = Convert-HexUInt32 $HexValue
    return [BitConverter]::ToSingle([BitConverter]::GetBytes($bits), 0)
}

$timelineFullPath = Resolve-FullPath $TimelineCsvPath
if (-not (Test-Path -LiteralPath $timelineFullPath)) {
    throw "Timeline CSV not found: $timelineFullPath"
}

$rows = @(Import-Csv -LiteralPath $timelineFullPath)
if ($rows.Count -eq 0) {
    throw "Timeline CSV has no rows: $timelineFullPath"
}

$packetSummaries = foreach ($packetGroup in @($rows | Group-Object packet)) {
    $packetRows = @($packetGroup.Group)
    foreach ($matrixGroup in @($packetRows | Group-Object transform_f0,transform_f1,transform_f2,transform_f3,transform_f4,transform_f5,transform_f6,transform_f7)) {
        $matrixRows = @($matrixGroup.Group)
        $sourceRecords = @($matrixRows | ForEach-Object { Convert-HexUInt32 $_.source_record })
        $fifoOffsets = @($matrixRows | ForEach-Object { Convert-HexUInt64 $_.gx_fifo_offset })
        $inputX = @($matrixRows | ForEach-Object { Get-FloatFromBits $_.input_x_bits })
        $inputY = @($matrixRows | ForEach-Object { Get-FloatFromBits $_.input_y_bits })
        $inputZ = @($matrixRows | ForEach-Object { Get-FloatFromBits $_.input_z_bits })
        $sourceX = @($matrixRows | ForEach-Object { Get-FloatFromBits $_.source_x })
        $sourceY = @($matrixRows | ForEach-Object { Get-FloatFromBits $_.source_y })
        $sourceZ = @($matrixRows | ForEach-Object { Get-FloatFromBits $_.source_z })

        [pscustomobject][ordered]@{
            packet = $packetGroup.Name
            rows = $matrixRows.Count
            stream1 = ($matrixRows | Select-Object -First 1).packet_stream1
            vertex_base = ($matrixRows | Select-Object -First 1).vertex_base
            first_fifo_offset = Format-HexValue (($fifoOffsets | Measure-Object -Minimum).Minimum)
            last_fifo_offset = Format-HexValue (($fifoOffsets | Measure-Object -Maximum).Maximum)
            first_source_record = Format-Hex32 (($sourceRecords | Measure-Object -Minimum).Minimum)
            last_source_record = Format-Hex32 (($sourceRecords | Measure-Object -Maximum).Maximum)
            input_min_x = (($inputX | Measure-Object -Minimum).Minimum)
            input_max_x = (($inputX | Measure-Object -Maximum).Maximum)
            input_min_y = (($inputY | Measure-Object -Minimum).Minimum)
            input_max_y = (($inputY | Measure-Object -Maximum).Maximum)
            input_min_z = (($inputZ | Measure-Object -Minimum).Minimum)
            input_max_z = (($inputZ | Measure-Object -Maximum).Maximum)
            source_min_x = (($sourceX | Measure-Object -Minimum).Minimum)
            source_max_x = (($sourceX | Measure-Object -Maximum).Maximum)
            source_min_y = (($sourceY | Measure-Object -Minimum).Minimum)
            source_max_y = (($sourceY | Measure-Object -Maximum).Maximum)
            source_min_z = (($sourceZ | Measure-Object -Minimum).Minimum)
            source_max_z = (($sourceZ | Measure-Object -Maximum).Maximum)
            transform_gqr1 = ($matrixRows | Select-Object -First 1).transform_gqr1
            transform_f0 = ($matrixRows | Select-Object -First 1).transform_f0
            transform_f1 = ($matrixRows | Select-Object -First 1).transform_f1
            transform_f2 = ($matrixRows | Select-Object -First 1).transform_f2
            transform_f3 = ($matrixRows | Select-Object -First 1).transform_f3
            transform_f4 = ($matrixRows | Select-Object -First 1).transform_f4
            transform_f5 = ($matrixRows | Select-Object -First 1).transform_f5
            transform_f6 = ($matrixRows | Select-Object -First 1).transform_f6
            transform_f7 = ($matrixRows | Select-Object -First 1).transform_f7
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($CsvPath)) {
    $csvFullPath = Resolve-FullPath $CsvPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $csvFullPath) | Out-Null
    $packetSummaries | Export-Csv -LiteralPath $csvFullPath -NoTypeInformation
}

$summary = [pscustomobject][ordered]@{
    timelineCsvPath = $timelineFullPath
    rowCount = $rows.Count
    packetCount = @($rows | Group-Object packet).Count
    matrixGroupCount = @($packetSummaries).Count
    topGroups = @($packetSummaries | Sort-Object rows -Descending | Select-Object -First $Top)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $jsonFullPath) | Out-Null
    $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonFullPath -Encoding UTF8
}

if ($PassThru) {
    $summary
} else {
    $summary | ConvertTo-Json -Depth 12
}
