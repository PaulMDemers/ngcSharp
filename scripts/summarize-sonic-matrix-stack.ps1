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

function Convert-WordToSingleOrNull {
    param($Word)

    if ($null -eq $Word) {
        return $null
    }

    return [BitConverter]::ToSingle([BitConverter]::GetBytes([uint32]$Word), 0)
}

function Format-Float {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    if ([double]::IsNaN([double]$Value) -or [double]::IsInfinity([double]$Value)) {
        return ([double]$Value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
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

function Get-FloatText {
    param(
        [uint32[]]$Words,
        [int]$Index
    )

    $word = Get-WordOrNull $Words $Index
    if ($null -eq $word) {
        return ""
    }

    return Format-Float (Convert-WordToSingleOrNull $word)
}

function Format-FloatVector {
    param(
        [uint32[]]$Words,
        [int[]]$Indices
    )

    if ($Words.Length -eq 0) {
        return ""
    }

    return (($Indices | ForEach-Object { Get-FloatText $Words $_ }) -join "/")
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Sonic matrix-stack trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "sonic-matrix-stack.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-matrix-stack.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Sonic matrix-stack trace CSV has no rows: $tracePath"
}

$summary = foreach ($row in $rows) {
    $r3Words = @(Convert-HexBytesToWords $row.r3_bytes)
    $r27Words = @(Convert-HexBytesToWords $row.r27_bytes)
    $r30Words = @(Convert-HexBytesToWords $row.r30_bytes)
    $baseMatrixWords = @(Convert-HexBytesToWords $row.base_matrix_bytes)
    $previousMatrixWords = @(Convert-HexBytesToWords $row.previous_matrix_bytes)
    $matrixWords = @(Convert-HexBytesToWords $row.current_matrix_bytes)

    $objectWord0 = Get-WordOrNull $r27Words 0
    $objectKind = if ($null -eq $objectWord0) { "" } else { "0x{0:X2}" -f ([uint32]$objectWord0 -band 0xFF) }
    $objectPacket = Format-Word (Get-WordOrNull $r27Words 1)
    $packetStream0 = Format-Word (Get-WordOrNull $r30Words 0)
    $packetStream1 = Format-Word (Get-WordOrNull $r30Words 1)

    [pscustomobject]@{
        instruction = [int64]$row.instruction
        pc = $row.pc
        lr = $row.lr
        matrix_base_pointer = $row.matrix_base_pointer
        previous_matrix_pointer = $row.previous_matrix_pointer
        current_matrix_pointer = $row.current_matrix_pointer
        r3 = $row.r3
        r27 = $row.r27
        r30 = $row.r30
        object_kind = $objectKind
        object_flags = Format-Word $objectWord0
        object_packet = $objectPacket
        object_transform_xyz = Format-FloatVector $r3Words @(0, 1, 2)
        object_extra_xyz = Format-FloatVector $r27Words @(2, 3, 4)
        object_scaleish = Format-FloatVector $r27Words @(8, 9, 10)
        packet_stream0 = $packetStream0
        packet_stream1 = $packetStream1
        base_matrix_translation = Format-FloatVector $baseMatrixWords @(3, 7, 11)
        previous_matrix_translation = Format-FloatVector $previousMatrixWords @(3, 7, 11)
        matrix_row0 = Format-FloatVector $matrixWords @(0, 1, 2, 3)
        matrix_row1 = Format-FloatVector $matrixWords @(4, 5, 6, 7)
        matrix_row2 = Format-FloatVector $matrixWords @(8, 9, 10, 11)
        matrix_translation = Format-FloatVector $matrixWords @(3, 7, 11)
        base_matrix_words = (($baseMatrixWords | ForEach-Object { Format-Word $_ }) -join " ")
        previous_matrix_words = (($previousMatrixWords | ForEach-Object { Format-Word $_ }) -join " ")
        matrix_words = (($matrixWords | ForEach-Object { Format-Word $_ }) -join " ")
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Sonic matrix-stack summary: $SummaryCsvPath"
$summary | Select-Object instruction,pc,lr,previous_matrix_pointer,current_matrix_pointer,r27,object_kind,object_packet,object_extra_xyz,previous_matrix_translation,matrix_translation | Format-Table -AutoSize
