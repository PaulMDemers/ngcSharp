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

function Convert-HexToBytes {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return [byte[]]::new(0)
    }

    $clean = $Hex.Trim()
    $bytes = [byte[]]::new([Math]::Floor($clean.Length / 2))
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [byte]::Parse($clean.Substring($i * 2, 2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $bytes
}

function Convert-HexBytesToWords {
    param([string]$Hex)

    $bytes = Convert-HexToBytes $Hex
    $wordCount = [Math]::Floor($bytes.Length / 4)
    $words = New-Object System.Collections.Generic.List[uint32]
    for ($i = 0; $i -lt $wordCount; $i++) {
        $words.Add(([uint32]$bytes[$i * 4] -shl 24) -bor ([uint32]$bytes[($i * 4) + 1] -shl 16) -bor ([uint32]$bytes[($i * 4) + 2] -shl 8) -bor [uint32]$bytes[($i * 4) + 3])
    }

    return $words.ToArray()
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

function Format-FloatVector {
    param(
        [uint32[]]$Words,
        [int[]]$Indices
    )

    if ($Words.Length -eq 0) {
        return ""
    }

    return (($Indices | ForEach-Object { Format-Float (Convert-WordToSingleOrNull (Get-WordOrNull $Words $_)) }) -join "/")
}

function Get-ShortHash {
    param([string]$Hex)

    $bytes = Convert-HexToBytes $Hex
    if ($bytes.Length -eq 0) {
        return ""
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $sha = $sha256.ComputeHash($bytes)
    } finally {
        $sha256.Dispose()
    }
    return (($sha[0..7] | ForEach-Object { $_.ToString("x2", [System.Globalization.CultureInfo]::InvariantCulture) }) -join "")
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Sonic scene-state trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "sonic-scene-state.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-scene-state.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Sonic scene-state trace CSV has no rows: $tracePath"
}

$summary = foreach ($row in $rows) {
    $packetWords = @(Convert-HexBytesToWords $row.packet_bytes)
    $objectWords = @(Convert-HexBytesToWords $row.object_bytes)

    [pscustomobject]@{
        instruction = [int64]$row.instruction
        packet = $row.packet
        packet_kind = $row.packet_kind
        object = $row.object
        object_kind = $row.object_kind
        object_packet = Format-Word (Get-WordOrNull $objectWords 1)
        stream0 = $row.stream0
        stream1 = $row.stream1
        vertex_base = $row.vertex_base
        object_xyz = Format-FloatVector $objectWords @(2, 3, 4)
        object_scaleish = Format-FloatVector $objectWords @(8, 9, 10)
        packet_word2_5 = Format-FloatVector $packetWords @(2, 3, 4, 5)
        resource_flag = $row.resource_flag
        state_byte13 = $row.state_byte13
        state_byte47 = $row.state_byte47
        state_word80 = $row.state_word80
        state_word_ec = $row.state_word_ec
        small_data_state = $row.small_data_state
        first_mode_flag = $row.first_mode_flag
        second_mode_flag = $row.second_mode_flag
        mode_pointer = $row.mode_pointer
        mode_pointer_value = $row.mode_pointer_value
        current_matrix_pointer = $row.current_matrix_pointer
        previous_matrix_pointer = $row.previous_matrix_pointer
        state_hash = Get-ShortHash $row.state_bytes
        small_data_hash = Get-ShortHash $row.small_data_bytes
        packet_hash = Get-ShortHash $row.packet_bytes
        object_hash = Get-ShortHash $row.object_bytes
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Wrote $($summary.Count) scene-state rows to $SummaryCsvPath"
