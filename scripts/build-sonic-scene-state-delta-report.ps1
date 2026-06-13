param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0"
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Get-ObjectValue {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Default = ""
    )

    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Test-CsvHasRows {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    return $null -ne (Import-Csv -LiteralPath $Path | Select-Object -First 1)
}

function Normalize-Hex {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith("+0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "+0x{0:X}" -f ([int64]::Parse($trimmed.Substring(3), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "0x{0:X}" -f ([int64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    return "0x{0:X}" -f ([int64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture))
}

function Convert-ToNullableInt64 {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [int64]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToByteArray {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return ,([byte[]]@())
    }

    $hex = ([string]$Value) -replace '[^0-9A-Fa-f]', ''
    if ($hex.Length % 2 -ne 0) {
        $hex = $hex.Substring(0, $hex.Length - 1)
    }

    $bytes = New-Object byte[] ($hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [byte]::Parse($hex.Substring($i * 2, 2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return ,$bytes
}

function Get-ShortHash {
    param([byte[]]$Bytes)

    if ($null -eq $Bytes -or $Bytes.Length -eq 0) {
        return ""
    }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($Bytes)
        return (($hash[0..7] | ForEach-Object { "{0:X2}" -f [int]$_ }) -join "").ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Format-HexPrefix {
    param(
        [byte[]]$Bytes,
        [int]$Length = 16
    )

    if ($null -eq $Bytes -or $Bytes.Length -eq 0) {
        return ""
    }

    $end = [Math]::Min($Length, $Bytes.Length) - 1
    return (($Bytes[0..$end] | ForEach-Object { "{0:X2}" -f [int]$_ }) -join "")
}

function Format-Byte {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return "0x{0:X2}" -f ([int]$Value)
}

function Format-Word {
    param([byte[]]$Bytes, [int]$Offset)

    if ($null -eq $Bytes -or $Offset -lt 0 -or $Offset + 3 -ge $Bytes.Length) {
        return ""
    }

    return "0x{0:X2}{1:X2}{2:X2}{3:X2}" -f ([int]$Bytes[$Offset], [int]$Bytes[$Offset + 1], [int]$Bytes[$Offset + 2], [int]$Bytes[$Offset + 3])
}

function Align-HexAddressDown {
    param(
        [string]$Address,
        [int64]$Alignment
    )

    if ([string]::IsNullOrWhiteSpace($Address) -or -not $Address.StartsWith("0x")) {
        return ""
    }

    $value = [int64]::Parse($Address.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    return "0x{0:X8}" -f ($value -band (-bnot ($Alignment - 1)))
}

function Get-UniqueStrings {
    param($Values)

    return @($Values | ForEach-Object { [string]$_ } | Sort-Object -Unique)
}

function Get-ChangeCount {
    param($Values)

    $count = 0
    $array = @($Values | ForEach-Object { [string]$_ })
    for ($i = 1; $i -lt $array.Count; $i++) {
        if ($array[$i] -ne $array[$i - 1]) {
            $count++
        }
    }

    return $count
}

function Get-BlobBaseAddress {
    param(
        [object]$Row,
        [string]$BlobName
    )

    switch ($BlobName) {
        "state_bytes" { return Normalize-Hex ([string](Get-ObjectValue $Row "state_base")) }
        "packet_bytes" { return Normalize-Hex ([string](Get-ObjectValue $Row "packet")) }
        "object_bytes" { return Normalize-Hex ([string](Get-ObjectValue $Row "object")) }
        "small_data_bytes" {
            $windowAddress = [string](Get-ObjectValue $Row "small_data_window_address")
            if (-not [string]::IsNullOrWhiteSpace($windowAddress)) {
                return Normalize-Hex $windowAddress
            }

            return Align-HexAddressDown (Normalize-Hex ([string](Get-ObjectValue $Row "small_data_state_address"))) 0x40
        }
        "mode_pointer_bytes" { return Normalize-Hex ([string](Get-ObjectValue $Row "mode_pointer_address")) }
        "current_matrix_bytes" { return Normalize-Hex ([string](Get-ObjectValue $Row "current_matrix_pointer")) }
        "previous_matrix_bytes" { return Normalize-Hex ([string](Get-ObjectValue $Row "previous_matrix_pointer")) }
        default { return "" }
    }
}

function Add-HexOffset {
    param(
        [string]$BaseAddress,
        [int]$Offset
    )

    if ([string]::IsNullOrWhiteSpace($BaseAddress) -or -not $BaseAddress.StartsWith("0x")) {
        return ""
    }

    $baseValue = [int64]::Parse($BaseAddress.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    return "0x{0:X8}" -f ($baseValue + $Offset)
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-scene-state-delta"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$sceneStateCsvPath = Join-Path $runRoot "sonic-scene-state.csv"
if (-not (Test-CsvHasRows $sceneStateCsvPath)) {
    throw "Required CSV missing or empty: $sceneStateCsvPath"
}

$focusPacket = Normalize-Hex $FocusPacket
$rows = @(
    Import-Csv -LiteralPath $sceneStateCsvPath |
        Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket } |
        Sort-Object @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "instruction") } }
)

if ($rows.Count -eq 0) {
    throw "No scene-state rows found for packet $focusPacket"
}

$blobFields = @(
    "packet_bytes",
    "object_bytes",
    "state_bytes",
    "small_data_bytes",
    "mode_pointer_bytes",
    "current_matrix_bytes",
    "previous_matrix_bytes"
)

$scalarRows = New-Object System.Collections.Generic.List[object]
$propertyNames = @($rows[0].PSObject.Properties.Name | Where-Object { $blobFields -notcontains $_ })
foreach ($name in $propertyNames) {
    $values = @($rows | ForEach-Object { [string](Get-ObjectValue $_ $name) })
    $unique = Get-UniqueStrings $values
    $changeCount = Get-ChangeCount $values
    $scalarRows.Add([pscustomobject][ordered]@{
        field = $name
        changed = if ($unique.Count -gt 1) { "True" } else { "False" }
        unique_count = $unique.Count
        change_count = $changeCount
        first_value = if ($values.Count -gt 0) { $values[0] } else { "" }
        last_value = if ($values.Count -gt 0) { $values[-1] } else { "" }
        unique_values = ($unique -join " ")
    })
}

$eventRows = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $rows.Count; $i++) {
    $row = $rows[$i]
    $stateBytes = Convert-ToByteArray (Get-ObjectValue $row "state_bytes")
    $smallDataBytes = Convert-ToByteArray (Get-ObjectValue $row "small_data_bytes")
    $currentMatrixBytes = Convert-ToByteArray (Get-ObjectValue $row "current_matrix_bytes")
    $previousMatrixBytes = Convert-ToByteArray (Get-ObjectValue $row "previous_matrix_bytes")
    $eventRows.Add([pscustomobject][ordered]@{
        event_index = $i
        instruction = Get-ObjectValue $row "instruction"
        packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
        object = Normalize-Hex ([string](Get-ObjectValue $row "object"))
        state_base = Normalize-Hex ([string](Get-ObjectValue $row "state_base"))
        state_hash = Get-ShortHash $stateBytes
        small_data_hash = Get-ShortHash $smallDataBytes
        current_matrix_hash = Get-ShortHash $currentMatrixBytes
        previous_matrix_hash = Get-ShortHash $previousMatrixBytes
        state_word80 = Get-ObjectValue $row "state_word80"
        state_word_ec = Get-ObjectValue $row "state_word_ec"
        small_data_state = Get-ObjectValue $row "small_data_state"
        current_matrix_pointer = Get-ObjectValue $row "current_matrix_pointer"
        previous_matrix_pointer = Get-ObjectValue $row "previous_matrix_pointer"
        current_matrix_prefix = Format-HexPrefix $currentMatrixBytes 16
    })
}

$byteRows = New-Object System.Collections.Generic.List[object]
$wordRows = New-Object System.Collections.Generic.List[object]
$blobSummaryRows = New-Object System.Collections.Generic.List[object]
foreach ($blob in $blobFields) {
    $arrays = @($rows | ForEach-Object { Convert-ToByteArray (Get-ObjectValue $_ $blob) })
    $maxLength = 0
    foreach ($bytes in $arrays) {
        $maxLength = [Math]::Max($maxLength, $bytes.Length)
    }

    if ($maxLength -eq 0) {
        continue
    }

    $baseAddress = Get-BlobBaseAddress $rows[0] $blob
    $changedBytes = 0
    for ($offset = 0; $offset -lt $maxLength; $offset++) {
        $values = @(
            $arrays | ForEach-Object {
                if ($offset -lt $_.Length) { Format-Byte $_[$offset] } else { "" }
            }
        )
        $unique = Get-UniqueStrings $values
        if ($unique.Count -le 1) {
            continue
        }

        $changedBytes++
        $changeInstructions = New-Object System.Collections.Generic.List[string]
        for ($i = 1; $i -lt $values.Count; $i++) {
            if ($values[$i] -ne $values[$i - 1]) {
                $changeInstructions.Add([string](Get-ObjectValue $rows[$i] "instruction"))
            }
        }

        $byteRows.Add([pscustomobject][ordered]@{
            blob = $blob
            offset = $offset
            offset_hex = "0x{0:X}" -f $offset
            address = Add-HexOffset $baseAddress $offset
            first_value = $values[0]
            last_value = $values[-1]
            unique_count = $unique.Count
            change_count = Get-ChangeCount $values
            unique_values = ($unique -join " ")
            change_instructions = ($changeInstructions -join " ")
        })
    }

    $changedWords = 0
    for ($offset = 0; $offset + 3 -lt $maxLength; $offset += 4) {
        $values = @($arrays | ForEach-Object { Format-Word $_ $offset })
        $unique = Get-UniqueStrings $values
        if ($unique.Count -le 1) {
            continue
        }

        $changedWords++
        $wordRows.Add([pscustomobject][ordered]@{
            blob = $blob
            offset = $offset
            offset_hex = "0x{0:X}" -f $offset
            address = Add-HexOffset $baseAddress $offset
            first_value = $values[0]
            last_value = $values[-1]
            unique_count = $unique.Count
            change_count = Get-ChangeCount $values
            unique_values = ($unique -join " ")
        })
    }

    $lengths = Get-UniqueStrings (@($arrays | ForEach-Object { $_.Length }))
    $blobSummaryRows.Add([pscustomobject][ordered]@{
        blob = $blob
        base_address = $baseAddress
        unique_lengths = ($lengths -join " ")
        changed_byte_count = $changedBytes
        changed_word_count = $changedWords
        total_compared_bytes = $maxLength
    })
}

$scalarCsvPath = Join-Path $OutputDirectory "scene-state-scalar-deltas.csv"
$eventsCsvPath = Join-Path $OutputDirectory "scene-state-events.csv"
$byteCsvPath = Join-Path $OutputDirectory "scene-state-byte-deltas.csv"
$wordCsvPath = Join-Path $OutputDirectory "scene-state-word-deltas.csv"
$blobSummaryCsvPath = Join-Path $OutputDirectory "scene-state-blob-summary.csv"
$summaryCsvPath = Join-Path $OutputDirectory "scene-state-delta-summary.csv"
$reportJsonPath = Join-Path $OutputDirectory "scene-state-delta-report.json"

$scalarRows | Export-Csv -LiteralPath $scalarCsvPath -NoTypeInformation
$eventRows | Export-Csv -LiteralPath $eventsCsvPath -NoTypeInformation
$byteRows | Export-Csv -LiteralPath $byteCsvPath -NoTypeInformation
$wordRows | Export-Csv -LiteralPath $wordCsvPath -NoTypeInformation
$blobSummaryRows | Export-Csv -LiteralPath $blobSummaryCsvPath -NoTypeInformation

$changedScalarRows = @($scalarRows | Where-Object { $_.changed -eq "True" })
$summary = [pscustomobject][ordered]@{
    focus_packet = $focusPacket
    event_count = $rows.Count
    first_instruction = Get-ObjectValue $rows[0] "instruction"
    last_instruction = Get-ObjectValue $rows[-1] "instruction"
    changed_scalar_fields = $changedScalarRows.Count
    changed_scalar_field_names = (($changedScalarRows | Select-Object -ExpandProperty field) -join " ")
    blobs_with_changes = (($blobSummaryRows | Where-Object { [int]$_.changed_byte_count -gt 0 } | Select-Object -ExpandProperty blob) -join " ")
    scalar_deltas_csv = $scalarCsvPath
    byte_deltas_csv = $byteCsvPath
    word_deltas_csv = $wordCsvPath
    blob_summary_csv = $blobSummaryCsvPath
}
$summary | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation

[pscustomobject][ordered]@{
    schema = "ngcsharp.sonic-scene-state-delta.v1"
    runDirectory = $runRoot
    focusPacket = $focusPacket
    generatedAt = (Get-Date).ToString("o")
    inputs = [ordered]@{
        sceneStateCsv = $sceneStateCsvPath
    }
    outputs = [ordered]@{
        scalarDeltasCsv = $scalarCsvPath
        eventsCsv = $eventsCsvPath
        byteDeltasCsv = $byteCsvPath
        wordDeltasCsv = $wordCsvPath
        blobSummaryCsv = $blobSummaryCsvPath
        summaryCsv = $summaryCsvPath
    }
    summary = $summary
    blobSummary = $blobSummaryRows
    changedScalarFields = $changedScalarRows
} | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $reportJsonPath

Write-Host "Wrote Sonic scene-state scalar deltas: $scalarCsvPath"
Write-Host "Wrote Sonic scene-state byte deltas: $byteCsvPath"
Write-Host "Wrote Sonic scene-state word deltas: $wordCsvPath"
Write-Host "Wrote Sonic scene-state delta summary: $summaryCsvPath"
