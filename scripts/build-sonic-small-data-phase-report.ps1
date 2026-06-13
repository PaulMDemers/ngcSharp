param(
    [string]$RunDirectory = "",
    [string]$SceneStateCsvPath = "",
    [string]$WriterDirectory = "",
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

function Convert-ToUInt64 {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [uint64]0
    }

    $trimmed = ([string]$Value).Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [uint64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [uint64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToInt64 {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return [int64]0
    }

    return [int64]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Normalize-Hex {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return "0x{0:X8}" -f ([uint32]((Convert-ToUInt64 $Value) -band 0xFFFFFFFF))
}

function Align-HexAddressDown {
    param(
        [string]$Address,
        [int64]$Alignment
    )

    if ([string]::IsNullOrWhiteSpace($Address)) {
        return ""
    }

    $value = [int64](Convert-ToUInt64 $Address)
    return "0x{0:X8}" -f ($value -band (-bnot ($Alignment - 1)))
}

function Convert-HexToBytes {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return ,([byte[]]@())
    }

    $clean = ([string]$Value) -replace '[^0-9A-Fa-f]', ''
    if ($clean.Length % 2 -ne 0) {
        $clean = $clean.Substring(0, $clean.Length - 1)
    }

    $bytes = New-Object byte[] ($clean.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [byte]::Parse($clean.Substring($i * 2, 2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return ,$bytes
}

function Read-WordFromBlob {
    param(
        [byte[]]$Bytes,
        [uint64]$BaseAddress,
        [uint64]$Address
    )

    if ($Address -lt $BaseAddress) {
        return ""
    }

    $offset = $Address - $BaseAddress
    if ($offset + 3 -ge [uint64]$Bytes.Length) {
        return ""
    }

    return "0x{0:X2}{1:X2}{2:X2}{3:X2}" -f [int]$Bytes[[int]$offset], [int]$Bytes[[int]($offset + 1)], [int]$Bytes[[int]($offset + 2)], [int]$Bytes[[int]($offset + 3)]
}

function Get-WriterRowsByEventTarget {
    param([string]$Directory)

    $lookup = @{}
    if ([string]::IsNullOrWhiteSpace($Directory)) {
        return $lookup
    }

    $path = Join-Path $Directory "scene-writer-to-focus-events.csv"
    if (-not (Test-CsvHasRows $path)) {
        return $lookup
    }

    foreach ($row in (Import-Csv -LiteralPath $path)) {
        $key = "{0}|{1}" -f (Get-ObjectValue $row "event_instruction"), (Get-ObjectValue $row "target")
        $lookup[$key] = $row
    }

    return $lookup
}

function Get-WriterValue {
    param(
        [hashtable]$Lookup,
        [object]$EventInstruction,
        [string]$Target,
        [string]$Field
    )

    $key = "{0}|{1}" -f $EventInstruction, $Target
    if (-not $Lookup.ContainsKey($key)) {
        return ""
    }

    return [string](Get-ObjectValue $Lookup[$key] $Field)
}

function Get-UniqueCount {
    param($Rows, [string]$Field)

    return @($Rows | ForEach-Object { [string](Get-ObjectValue $_ $Field) } | Sort-Object -Unique).Count
}

$runRoot = ""
if (-not [string]::IsNullOrWhiteSpace($RunDirectory)) {
    $runRoot = Resolve-FullPath $RunDirectory
    if (-not (Test-Path -LiteralPath $runRoot)) {
        throw "Run directory not found: $runRoot"
    }

    if ([string]::IsNullOrWhiteSpace($SceneStateCsvPath)) {
        $SceneStateCsvPath = Join-Path $runRoot "sonic-scene-state.csv"
    }

    if ([string]::IsNullOrWhiteSpace($WriterDirectory)) {
        $candidate = Join-Path $runRoot "sonic-scene-writers"
        if (Test-Path -LiteralPath $candidate) {
            $WriterDirectory = $candidate
        }
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $runRoot "sonic-small-data-phase"
    }
}

if ([string]::IsNullOrWhiteSpace($SceneStateCsvPath)) {
    throw "SceneStateCsvPath or RunDirectory is required."
}

$sceneStatePath = Resolve-FullPath $SceneStateCsvPath
if (-not (Test-CsvHasRows $sceneStatePath)) {
    throw "Scene-state CSV missing or empty: $sceneStatePath"
}

if (-not [string]::IsNullOrWhiteSpace($WriterDirectory)) {
    $WriterDirectory = Resolve-FullPath $WriterDirectory
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $sceneStatePath) "sonic-small-data-phase"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$focusPacket = Normalize-Hex $FocusPacket
$writerLookup = Get-WriterRowsByEventTarget $WriterDirectory
$targetDefinitions = @(
    [pscustomobject]@{ Name = "small-data-ptr"; Address = Convert-ToUInt64 "0x803ADC84" },
    [pscustomobject]@{ Name = "small-data-word-88"; Address = Convert-ToUInt64 "0x803ADC88" },
    [pscustomobject]@{ Name = "small-data-timer"; Address = Convert-ToUInt64 "0x803ADC94" },
    [pscustomobject]@{ Name = "small-data-flag"; Address = Convert-ToUInt64 "0x803ADCE4" }
)

$sceneRows = @(
    Import-Csv -LiteralPath $sceneStatePath |
        Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket } |
        Sort-Object @{ Expression = { Convert-ToInt64 (Get-ObjectValue $_ "instruction") } }
)

if ($sceneRows.Count -eq 0) {
    throw "No scene-state rows found for focus packet $focusPacket"
}

$eventRows = New-Object System.Collections.Generic.List[object]
$previousValues = @{}
for ($i = 0; $i -lt $sceneRows.Count; $i++) {
    $row = $sceneRows[$i]
    $instruction = [string](Get-ObjectValue $row "instruction")
    $smallDataWindowAddress = Normalize-Hex ([string](Get-ObjectValue $row "small_data_window_address"))
    if ([string]::IsNullOrWhiteSpace($smallDataWindowAddress)) {
        $smallDataWindowAddress = Align-HexAddressDown (Normalize-Hex ([string](Get-ObjectValue $row "small_data_state_address"))) 0x40
    }

    $smallDataWindowBase = Convert-ToUInt64 $smallDataWindowAddress
    $smallDataBytes = Convert-HexToBytes (Get-ObjectValue $row "small_data_bytes")
    $values = @{}
    $changedNames = New-Object System.Collections.Generic.List[string]
    foreach ($target in $targetDefinitions) {
        $value = Read-WordFromBlob $smallDataBytes $smallDataWindowBase $target.Address
        $values[$target.Name] = $value
        if ($previousValues.ContainsKey($target.Name) -and [string]$previousValues[$target.Name] -ne $value) {
            $changedNames.Add($target.Name)
        }

        $previousValues[$target.Name] = $value
    }

    $eventRows.Add([pscustomobject][ordered]@{
        event_index = $i
        instruction = $instruction
        packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
        object = Normalize-Hex ([string](Get-ObjectValue $row "object"))
        pc = Normalize-Hex ([string](Get-ObjectValue $row "pc"))
        lr = Normalize-Hex ([string](Get-ObjectValue $row "lr"))
        small_data_state_address = Normalize-Hex ([string](Get-ObjectValue $row "small_data_state_address"))
        small_data_window_address = $smallDataWindowAddress
        small_data_ptr = $values["small-data-ptr"]
        small_data_word_88 = $values["small-data-word-88"]
        small_data_timer = $values["small-data-timer"]
        small_data_flag = $values["small-data-flag"]
        changed_since_previous_event = ($changedNames -join " ")
        ptr_last_write_pc = Get-WriterValue $writerLookup $instruction "small-data-ptr" "pc"
        ptr_last_write_instruction = Get-WriterValue $writerLookup $instruction "small-data-ptr" "last_write_instruction"
        ptr_last_change_instruction = Get-WriterValue $writerLookup $instruction "small-data-ptr" "last_change_instruction"
        word88_last_write_pc = Get-WriterValue $writerLookup $instruction "small-data-word-88" "pc"
        word88_last_write_instruction = Get-WriterValue $writerLookup $instruction "small-data-word-88" "last_write_instruction"
        word88_last_change_instruction = Get-WriterValue $writerLookup $instruction "small-data-word-88" "last_change_instruction"
        timer_last_write_pc = Get-WriterValue $writerLookup $instruction "small-data-timer" "pc"
        timer_last_write_instruction = Get-WriterValue $writerLookup $instruction "small-data-timer" "last_write_instruction"
        timer_last_change_instruction = Get-WriterValue $writerLookup $instruction "small-data-timer" "last_change_instruction"
        flag_last_write_pc = Get-WriterValue $writerLookup $instruction "small-data-flag" "pc"
        flag_last_write_instruction = Get-WriterValue $writerLookup $instruction "small-data-flag" "last_write_instruction"
        flag_last_change_instruction = Get-WriterValue $writerLookup $instruction "small-data-flag" "last_change_instruction"
        state_word80 = [string](Get-ObjectValue $row "state_word80")
        state_word_ec = [string](Get-ObjectValue $row "state_word_ec")
        current_matrix_pointer = Normalize-Hex ([string](Get-ObjectValue $row "current_matrix_pointer"))
        previous_matrix_pointer = Normalize-Hex ([string](Get-ObjectValue $row "previous_matrix_pointer"))
    })
}

$eventCsvPath = Join-Path $OutputDirectory "small-data-phase-events.csv"
$summaryCsvPath = Join-Path $OutputDirectory "small-data-phase-summary.csv"
$reportJsonPath = Join-Path $OutputDirectory "small-data-phase-report.json"

$eventRows | Export-Csv -LiteralPath $eventCsvPath -NoTypeInformation

$summary = [pscustomobject][ordered]@{
    focus_packet = $focusPacket
    event_count = $eventRows.Count
    first_instruction = $eventRows[0].instruction
    last_instruction = $eventRows[-1].instruction
    unique_small_data_ptr = Get-UniqueCount $eventRows "small_data_ptr"
    unique_small_data_word_88 = Get-UniqueCount $eventRows "small_data_word_88"
    unique_small_data_timer = Get-UniqueCount $eventRows "small_data_timer"
    unique_small_data_flag = Get-UniqueCount $eventRows "small_data_flag"
    writer_directory = $WriterDirectory
    events_csv = $eventCsvPath
}
$summary | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation

[pscustomobject][ordered]@{
    schema = "ngcsharp.sonic-small-data-phase.v1"
    generatedAt = (Get-Date).ToString("o")
    runDirectory = $runRoot
    inputs = [ordered]@{
        sceneStateCsv = $sceneStatePath
        writerDirectory = $WriterDirectory
    }
    outputs = [ordered]@{
        eventsCsv = $eventCsvPath
        summaryCsv = $summaryCsvPath
    }
    summary = $summary
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportJsonPath

Write-Host "Wrote Sonic small-data phase events: $eventCsvPath"
Write-Host "Wrote Sonic small-data phase summary: $summaryCsvPath"
