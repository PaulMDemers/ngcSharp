param(
    [Parameter(Mandatory = $true)]
    [string]$LockedCacheCsvPath,
    [string]$RangeBaseAddress = "0xE0000030",
    [string]$TimelineCsvPath = "",
    [string]$TimelineJsonPath = ""
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

function Convert-HexToBytes {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return [byte[]]::new(0)
    }

    $text = $Hex.Trim()
    $bytes = [byte[]]::new([Math]::Floor($text.Length / 2))
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [byte]::Parse($text.Substring($index * 2, 2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $bytes
}

function Read-BeUInt32 {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    if ($Offset -lt 0 -or ($Offset + 4) -gt $Bytes.Length) {
        return $null
    }

    return [uint32]((([uint32]$Bytes[$Offset]) -shl 24) -bor ([uint32]$Bytes[$Offset + 1] -shl 16) -bor ([uint32]$Bytes[$Offset + 2] -shl 8) -bor [uint32]$Bytes[$Offset + 3])
}

function Convert-WordToFloat {
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

function Get-MatrixWords {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    $words = New-Object System.Collections.Generic.List[uint32]
    for ($wordIndex = 0; $wordIndex -lt 12; $wordIndex++) {
        $word = Read-BeUInt32 $Bytes ($Offset + ($wordIndex * 4))
        if ($null -eq $word) {
            return @()
        }

        $words.Add($word)
    }

    return $words.ToArray()
}

function Format-Vector {
    param(
        [uint32[]]$Words,
        [int[]]$Indices
    )

    if ($Words.Count -lt 12) {
        return ""
    }

    return (($Indices | ForEach-Object { Format-Float (Convert-WordToFloat $Words[$_]) }) -join "/")
}

function Get-Phase {
    param([string]$Pc)

    switch ($Pc) {
        "0x8010C0C4" { return "copy_lower_slot_terminal" }
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

$tracePath = Resolve-FullPath $LockedCacheCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Locked-cache trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($TimelineCsvPath)) {
    $TimelineCsvPath = Join-Path $directory "sonic-matrix-producer-timeline.csv"
} else {
    $TimelineCsvPath = Resolve-FullPath $TimelineCsvPath
}

if ([string]::IsNullOrWhiteSpace($TimelineJsonPath)) {
    $TimelineJsonPath = Join-Path $directory "sonic-matrix-producer-timeline.json"
} else {
    $TimelineJsonPath = Resolve-FullPath $TimelineJsonPath
}

$rangeBase = Parse-HexOrDecimal $RangeBaseAddress
$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Locked-cache trace CSV has no rows: $tracePath"
}

$timeline = foreach ($row in $rows) {
    $bytes = Convert-HexToBytes $row.range_bytes
    $slot0 = @(Get-MatrixWords $bytes 0x00)
    $slot1 = @(Get-MatrixWords $bytes 0x30)
    $slot2 = @(Get-MatrixWords $bytes 0x60)
    $slot3 = @(Get-MatrixWords $bytes 0x90)
    $address = Parse-HexOrDecimal $row.address
    $slotIndex = if ($address -ge $rangeBase) { [int](($address - $rangeBase) / 0x30) } else { -1 }

    [pscustomobject]@{
        instruction = [int64]$row.instruction
        pc = $row.pc
        phase = Get-Phase $row.pc
        disassembly = $row.disassembly
        address = $row.address
        slot = $slotIndex
        value = $row.value
        r3 = $row.r3
        r4 = $row.r4
        r5 = $row.r5
        r6 = $row.r6
        r27 = $row.r27
        r30 = $row.r30
        r31 = $row.r31
        slot0_translation = Format-Vector $slot0 @(3, 7, 11)
        slot1_translation = Format-Vector $slot1 @(3, 7, 11)
        slot2_translation = Format-Vector $slot2 @(3, 7, 11)
        slot3_translation = Format-Vector $slot3 @(3, 7, 11)
        slot0_row0 = Format-Vector $slot0 @(0, 1, 2, 3)
        slot1_row0 = Format-Vector $slot1 @(0, 1, 2, 3)
        slot2_row0 = Format-Vector $slot2 @(0, 1, 2, 3)
        slot3_row0 = Format-Vector $slot3 @(0, 1, 2, 3)
        slot0_row1 = Format-Vector $slot0 @(4, 5, 6, 7)
        slot1_row1 = Format-Vector $slot1 @(4, 5, 6, 7)
        slot2_row1 = Format-Vector $slot2 @(4, 5, 6, 7)
        slot3_row1 = Format-Vector $slot3 @(4, 5, 6, 7)
        slot0_row2 = Format-Vector $slot0 @(8, 9, 10, 11)
        slot1_row2 = Format-Vector $slot1 @(8, 9, 10, 11)
        slot2_row2 = Format-Vector $slot2 @(8, 9, 10, 11)
        slot3_row2 = Format-Vector $slot3 @(8, 9, 10, 11)
    }
}

$terminalRows = @($timeline |
    Where-Object { $_.phase -like "*terminal" -or $_.phase -eq "transpose_terminal" } |
    Group-Object instruction, pc, phase |
    ForEach-Object { $_.Group | Select-Object -Last 1 })

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TimelineCsvPath) | Out-Null
$timeline | Export-Csv -LiteralPath $TimelineCsvPath -NoTypeInformation
[pscustomobject]@{
    traceCsvPath = $tracePath
    timelineCsvPath = $TimelineCsvPath
    rowCount = $timeline.Count
    terminalRowCount = $terminalRows.Count
    terminalRows = @($terminalRows | Select-Object instruction, pc, phase, r3, r4, r5, r6, slot0_translation, slot1_translation, slot2_translation, slot3_translation)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $TimelineJsonPath

Write-Host "Sonic matrix producer timeline: $TimelineCsvPath"
$terminalRows | Select-Object instruction,pc,phase,r3,r5,r6,slot0_translation,slot1_translation,slot2_translation,slot3_translation | Format-Table -AutoSize
