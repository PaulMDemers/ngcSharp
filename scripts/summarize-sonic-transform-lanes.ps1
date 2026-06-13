param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$OutputCsvPath = "",
    [string]$OutputJsonPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Read-DoubleOrNull {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    return [double]::Parse($Text.Trim(), [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Test-FiniteNumber {
    param($Value)

    if ($null -eq $Value) {
        return $false
    }

    $number = [double]$Value
    return -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)
}

function Split-FprPair {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @($null, $null)
    }

    $parts = $Value -split '\|', 2
    if ($parts.Count -lt 2) {
        return @((Read-DoubleOrNull $parts[0]), $null)
    }

    return @((Read-DoubleOrNull $parts[0]), (Read-DoubleOrNull $parts[1]))
}

function Test-VectorFinite {
    param([object[]]$Values)

    foreach ($value in $Values) {
        if (-not (Test-FiniteNumber $value)) {
            return $false
        }
    }

    return $true
}

function Format-Double {
    param($Value)

    if (-not (Test-FiniteNumber $Value)) {
        return ""
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Vector {
    param([object[]]$Values)

    return (($Values | ForEach-Object { Format-Double $_ }) -join "/")
}

function Get-Length3 {
    param([object[]]$Vector)

    if (-not (Test-VectorFinite @($Vector[0], $Vector[1], $Vector[2]))) {
        return $null
    }

    return [Math]::Sqrt(
        ([double]$Vector[0] * [double]$Vector[0]) +
        ([double]$Vector[1] * [double]$Vector[1]) +
        ([double]$Vector[2] * [double]$Vector[2]))
}

function Get-Dot3 {
    param(
        [object[]]$A,
        [object[]]$B
    )

    if (-not (Test-VectorFinite @($A[0], $A[1], $A[2], $B[0], $B[1], $B[2]))) {
        return $null
    }

    return ([double]$A[0] * [double]$B[0]) +
        ([double]$A[1] * [double]$B[1]) +
        ([double]$A[2] * [double]$B[2])
}

function Get-MaxError {
    param([object[]]$Values)

    $finiteValues = @($Values | Where-Object { Test-FiniteNumber $_ })
    if ($finiteValues.Count -eq 0) {
        return $null
    }

    return ($finiteValues | Measure-Object -Maximum).Maximum
}

function New-BasisMetrics {
    param(
        [object[]]$RowX,
        [object[]]$RowY,
        [object[]]$RowZ
    )

    if (-not (Test-VectorFinite @($RowX[0], $RowX[1], $RowX[2], $RowY[0], $RowY[1], $RowY[2], $RowZ[0], $RowZ[1], $RowZ[2]))) {
        return [pscustomobject]@{
            row_x_length = ""
            row_y_length = ""
            row_z_length = ""
            dot_xy = ""
            dot_xz = ""
            dot_yz = ""
            max_length_error = ""
            max_dot_error = ""
        }
    }

    $lenX = Get-Length3 $RowX
    $lenY = Get-Length3 $RowY
    $lenZ = Get-Length3 $RowZ
    $dotXY = Get-Dot3 $RowX $RowY
    $dotXZ = Get-Dot3 $RowX $RowZ
    $dotYZ = Get-Dot3 $RowY $RowZ
    $maxLengthError = Get-MaxError @(
        [Math]::Abs($lenX - 1.0),
        [Math]::Abs($lenY - 1.0),
        [Math]::Abs($lenZ - 1.0)
    )
    $maxDotError = Get-MaxError @([Math]::Abs($dotXY), [Math]::Abs($dotXZ), [Math]::Abs($dotYZ))

    return [pscustomobject]@{
        row_x_length = Format-Double $lenX
        row_y_length = Format-Double $lenY
        row_z_length = Format-Double $lenZ
        dot_xy = Format-Double $dotXY
        dot_xz = Format-Double $dotXZ
        dot_yz = Format-Double $dotYZ
        max_length_error = Format-Double $maxLengthError
        max_dot_error = Format-Double $maxDotError
    }
}

function New-EmptyBasisMetrics {
    return [pscustomobject]@{
        row_x_length = ""
        row_y_length = ""
        row_z_length = ""
        dot_xy = ""
        dot_xz = ""
        dot_yz = ""
        max_length_error = ""
        max_dot_error = ""
    }
}

function Get-Phase {
    param([string]$Pc)

    switch ($Pc) {
        "0x801160DC" { return "copy_parent_terminal" }
        "0x80116090" { return "copy_identity_terminal" }
        "0x80116164" { return "rotation_terminal" }
        "0x801161C4" { return "translation_terminal" }
        "0x8011C184" { return "transpose_terminal" }
        default {
            if ($Pc -like "0x8011C1*") { return "transpose" }
            if ($Pc -like "0x801160*" -or $Pc -like "0x801161*") { return "matrix_stack" }
            return "write"
        }
    }
}

function Convert-ToDoubleOrBlank {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    return [double]::Parse($Text, [System.Globalization.CultureInfo]::InvariantCulture)
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Sonic matrix-writer trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $directory "sonic-transform-lanes.csv"
} else {
    $OutputCsvPath = Resolve-FullPath $OutputCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $directory "sonic-transform-lanes.json"
} else {
    $OutputJsonPath = Resolve-FullPath $OutputJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Sonic matrix-writer trace CSV has no rows: $tracePath"
}

$diagnostics = foreach ($row in $rows) {
    $f0 = @(Split-FprPair $row.f0)
    $f1 = @(Split-FprPair $row.f1)
    $f2 = @(Split-FprPair $row.f2)
    $f3 = @(Split-FprPair $row.f3)
    $f4 = @(Split-FprPair $row.f4)
    $f5 = @(Split-FprPair $row.f5)
    $f6 = @(Split-FprPair $row.f6)
    $f7 = @(Split-FprPair $row.f7)
    $f8 = @(Split-FprPair $row.f8)
    $f9 = @(Split-FprPair $row.f9)
    $f10 = @(Split-FprPair $row.f10)
    $f12 = @(Split-FprPair $row.f12)

    $phase = Get-Phase $row.pc
    $sourceValid = $phase -eq "copy_parent_terminal" -or
        $phase -eq "copy_identity_terminal" -or
        $phase -eq "rotation_terminal" -or
        $phase -eq "translation_terminal" -or
        $phase -eq "transpose" -or
        $phase -eq "transpose_terminal"
    $packedValid = $phase -eq "transpose_terminal"

    $sourceRowX = @($f0[0], $f0[1], $f1[0], $f1[1])
    $sourceRowY = @($f2[0], $f2[1], $f3[0], $f3[1])
    $sourceRowZ = @($f4[0], $f4[1], $f5[0], $f5[1])
    $sourceMetrics = if ($sourceValid) { New-BasisMetrics $sourceRowX $sourceRowY $sourceRowZ } else { New-EmptyBasisMetrics }

    $packedRowX = @($f6[0], $f7[0], $f8[0], $f9[1])
    $packedRowY = @($f6[1], $f12[1], $f8[1], $f10[0])
    $packedRowZ = @($f12[0], $f7[1], $f9[0], $f10[1])
    $packedMetrics = if ($packedValid) { New-BasisMetrics $packedRowX $packedRowY $packedRowZ } else { New-EmptyBasisMetrics }

    [pscustomobject]@{
        instruction = [int64]$row.instruction
        pc = $row.pc
        phase = $phase
        store_address = $row.store_address
        object = $row.r27
        packet = $row.r30
        r3 = $row.r3
        r4 = $row.r4
        r5 = $row.r5
        r6 = $row.r6
        source_translation = if ($sourceValid) { Format-Vector @($f1[1], $f3[1], $f5[1]) } else { "" }
        source_row_x = if ($sourceValid) { Format-Vector $sourceRowX } else { "" }
        source_row_y = if ($sourceValid) { Format-Vector $sourceRowY } else { "" }
        source_row_z = if ($sourceValid) { Format-Vector $sourceRowZ } else { "" }
        source_row_x_length = $sourceMetrics.row_x_length
        source_row_y_length = $sourceMetrics.row_y_length
        source_row_z_length = $sourceMetrics.row_z_length
        source_dot_xy = $sourceMetrics.dot_xy
        source_dot_xz = $sourceMetrics.dot_xz
        source_dot_yz = $sourceMetrics.dot_yz
        source_max_length_error = $sourceMetrics.max_length_error
        source_max_dot_error = $sourceMetrics.max_dot_error
        packed_translation = if ($packedValid) { Format-Vector @($f9[1], $f10[0], $f10[1]) } else { "" }
        packed_row_x = if ($packedValid) { Format-Vector $packedRowX } else { "" }
        packed_row_y = if ($packedValid) { Format-Vector $packedRowY } else { "" }
        packed_row_z = if ($packedValid) { Format-Vector $packedRowZ } else { "" }
        packed_row_x_length = $packedMetrics.row_x_length
        packed_row_y_length = $packedMetrics.row_y_length
        packed_row_z_length = $packedMetrics.row_z_length
        packed_dot_xy = $packedMetrics.dot_xy
        packed_dot_xz = $packedMetrics.dot_xz
        packed_dot_yz = $packedMetrics.dot_yz
        packed_max_length_error = $packedMetrics.max_length_error
        packed_max_dot_error = $packedMetrics.max_dot_error
        disassembly = $row.disassembly
    }
}

$interesting = @(
    $diagnostics |
        Where-Object {
            $_.phase -like "*terminal" -or
            ([string]$_.source_max_length_error -ne "" -and [double]$_.source_max_length_error -gt 0.05) -or
            ([string]$_.source_max_dot_error -ne "" -and [double]$_.source_max_dot_error -gt 0.05) -or
            ([string]$_.packed_max_length_error -ne "" -and [double]$_.packed_max_length_error -gt 0.05) -or
            ([string]$_.packed_max_dot_error -ne "" -and [double]$_.packed_max_dot_error -gt 0.05)
        } |
        Sort-Object instruction
)

$firstSourceNonOrthogonal = @(
    $diagnostics |
        Where-Object {
            ([string]$_.source_max_length_error -ne "" -and [double]$_.source_max_length_error -gt 0.05) -or
            ([string]$_.source_max_dot_error -ne "" -and [double]$_.source_max_dot_error -gt 0.05)
        } |
        Sort-Object instruction |
        Select-Object -First 1
)

$firstPackedNonOrthogonal = @(
    $diagnostics |
        Where-Object {
            ([string]$_.packed_max_length_error -ne "" -and [double]$_.packed_max_length_error -gt 0.05) -or
            ([string]$_.packed_max_dot_error -ne "" -and [double]$_.packed_max_dot_error -gt 0.05)
        } |
        Sort-Object instruction |
        Select-Object -First 1
)

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputCsvPath) | Out-Null
$interesting | Export-Csv -LiteralPath $OutputCsvPath -NoTypeInformation

[pscustomobject]@{
    traceCsvPath = $tracePath
    outputCsvPath = $OutputCsvPath
    inputRows = $rows.Count
    diagnosticRows = @($diagnostics).Count
    interestingRows = $interesting.Count
    firstSourceNonOrthogonal = $firstSourceNonOrthogonal
    firstPackedNonOrthogonal = $firstPackedNonOrthogonal
    terminalRows = @($diagnostics | Where-Object { $_.phase -like "*terminal" } | Select-Object -First 20)
    worstSourceRows = @(
        $diagnostics |
            Where-Object { [string]$_.source_max_dot_error -ne "" -or [string]$_.source_max_length_error -ne "" } |
            Sort-Object @{ Expression = { Convert-ToDoubleOrBlank $_.source_max_dot_error }; Descending = $true }, @{ Expression = { Convert-ToDoubleOrBlank $_.source_max_length_error }; Descending = $true } |
            Select-Object -First 12
    )
    worstPackedRows = @(
        $diagnostics |
            Where-Object { [string]$_.packed_max_dot_error -ne "" -or [string]$_.packed_max_length_error -ne "" } |
            Sort-Object @{ Expression = { Convert-ToDoubleOrBlank $_.packed_max_dot_error }; Descending = $true }, @{ Expression = { Convert-ToDoubleOrBlank $_.packed_max_length_error }; Descending = $true } |
            Select-Object -First 12
    )
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath

Write-Host "Sonic transform lane summary: $OutputCsvPath"
Write-Host "Interesting rows: $($interesting.Count)"
$interesting |
    Select-Object -First 24 instruction,pc,phase,object,packet,source_translation,source_row_x_length,source_row_y_length,source_row_z_length,source_dot_xy,source_dot_xz,source_dot_yz,packed_translation |
    Format-Table -AutoSize
