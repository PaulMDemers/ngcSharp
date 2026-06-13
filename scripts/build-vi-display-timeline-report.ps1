param(
    [Parameter(Mandatory = $true)]
    [string]$RunRoot,
    [string]$GxCopiesPath = "",
    [string]$MmioTracePath = "",
    [string]$GxFrameSweepPath = "",
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

function Get-ObjectValue {
    param($Object, [string]$Name, $Default = "")

    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }

    return $property.Value
}

function Convert-ToNullableInt64 {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    $text = "$Value".Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    if ($text.StartsWith("+0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToInt64($text.Substring(3), 16)
    }

    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToInt64($text.Substring(2), 16)
    }

    $parsed = 0L
    if ([long]::TryParse($text, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Convert-ToNullableUInt32 {
    param($Value)

    $parsed = Convert-ToNullableInt64 $Value
    if ($null -eq $parsed) {
        return $null
    }

    if ($parsed -lt 0) {
        $parsed += 0x1_0000_0000
    }

    if ($parsed -lt 0 -or $parsed -gt [uint32]::MaxValue) {
        return $null
    }

    return [uint32]$parsed
}

function Convert-ToUInt32Number {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    $number = [int64]$Value
    if ($number -lt 0) {
        $number += 0x1_0000_0000
    }

    if ($number -lt 0 -or $number -gt [uint32]::MaxValue) {
        return $null
    }

    return $number
}

function Format-Hex32 {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    $number = Convert-ToUInt32Number $Value
    if ($null -eq $number) {
        return ""
    }

    return "0x{0:X8}" -f $number
}

function Format-FifoOffset {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return "+0x{0:X}" -f ([int64]$Value)
}

function Test-MainRamPhysicalAddress {
    param($Address)

    return $Address -gt 0 -and $Address -lt 0x01800000
}

function Convert-ToPhysicalAddress {
    param($Address)

    $Address = Convert-ToUInt32Number $Address
    if ($null -eq $Address) {
        return $null
    }

    if ($Address -ge 0x80000000 -and $Address -lt 0x81800000) {
        return $Address - 0x80000000
    }

    if ($Address -ge 0xC0000000 -and $Address -lt 0xC1800000) {
        return $Address - 0xC0000000
    }

    return $Address
}

function Normalize-ViAddress {
    param($Value, [bool]$PreferShifted)

    $physical = Convert-ToPhysicalAddress $Value
    $number = Convert-ToUInt32Number $Value
    if ($null -eq $number) {
        return $null
    }

    $shifted = Convert-ToPhysicalAddress (($number -band 0x00FFFFFF) -shl 5)

    if ($null -ne $shifted -and $PreferShifted -and $shifted -ne $physical -and (Test-MainRamPhysicalAddress $shifted)) {
        return $shifted
    }

    if ($null -ne $physical -and (Test-MainRamPhysicalAddress $physical)) {
        return $physical
    }

    if ($null -ne $shifted -and -not $PreferShifted -and $shifted -ne $physical -and (Test-MainRamPhysicalAddress $shifted)) {
        return $shifted
    }

    return $null
}

function Get-ViRegisterName {
    param($Address)

    switch (Format-Hex32 $Address) {
        "0xCC00201C" { "field0_high" }
        "0xCC00201E" { "field0_low" }
        "0xCC002024" { "field1_high" }
        "0xCC002026" { "field1_low" }
        "0xCC002020" { "field2_high" }
        "0xCC002022" { "field2_low" }
        "0xCC002028" { "field3_high" }
        "0xCC00202A" { "field3_low" }
        default { "" }
    }
}

function Get-ViPairs {
    return @(
        [pscustomobject]@{ name = "field0"; high = "0xCC00201C"; low = "0xCC00201E" },
        [pscustomobject]@{ name = "field1"; high = "0xCC002024"; low = "0xCC002026" },
        [pscustomobject]@{ name = "field2"; high = "0xCC002020"; low = "0xCC002022" },
        [pscustomobject]@{ name = "field3"; high = "0xCC002028"; low = "0xCC00202A" }
    )
}

function Get-ViResolvedAddresses {
    param([hashtable]$RegisterValues)

    $pairs = Get-ViPairs
    $result = [ordered]@{}
    foreach ($pair in $pairs) {
        $address = $null
        if ($RegisterValues.ContainsKey($pair.high) -and $RegisterValues.ContainsKey($pair.low)) {
            $combined = (($RegisterValues[$pair.high] -band 0xFF) -shl 16) -bor ($RegisterValues[$pair.low] -band 0xFFFF)
            $address = Normalize-ViAddress $combined $false
        }

        $result[$pair.name] = $address
    }

    foreach ($register in @("0xCC00201C", "0xCC002024", "0xCC002020", "0xCC002028")) {
        if ($RegisterValues.ContainsKey($register)) {
            $direct = Normalize-ViAddress $RegisterValues[$register] $true
            if ($null -ne $direct) {
                $name = (Get-ViRegisterName $register).Replace("_high", "")
                if (-not [string]::IsNullOrWhiteSpace($name) -and $null -eq $result[$name]) {
                    $result[$name] = $direct
                }
            }
        }
    }

    return $result
}

function Convert-CopyRow {
    param($Row)

    if ((Get-ObjectValue $Row "kind") -ne "display") {
        return $null
    }

    $fifoOffset = Convert-ToNullableInt64 (Get-ObjectValue $Row "fifo_offset")
    $address = Convert-ToNullableUInt32 (Get-ObjectValue $Row "display_address" (Get-ObjectValue $Row "destination_address"))
    return [pscustomobject]@{
        copy_index = Convert-ToNullableInt64 (Get-ObjectValue $Row "copy_index")
        fifo_offset = $fifoOffset
        fifo_offset_text = Format-FifoOffset $fifoOffset
        draws_seen = Convert-ToNullableInt64 (Get-ObjectValue $Row "draws_seen")
        display_address = $address
        display_address_text = Format-Hex32 $address
        display_nonblack = Convert-ToNullableInt64 (Get-ObjectValue $Row "display_nonblack")
        display_nonblack_percent = Get-ObjectValue $Row "display_nonblack_percent"
        display_nonblack_bounds = Get-ObjectValue $Row "display_nonblack_bounds"
        source_width = Get-ObjectValue $Row "src_width"
        source_height = Get-ObjectValue $Row "src_height"
        display_width = Get-ObjectValue $Row "display_width"
        display_height = Get-ObjectValue $Row "display_height"
        clear = Get-ObjectValue $Row "clear"
        instruction = $null
        pc = ""
        opcode = ""
        disassembly = ""
        vi_field = ""
        vi_field0 = ""
        vi_field1 = ""
        vi_field2 = ""
        vi_field3 = ""
    }
}

function Read-MmioTraceRows {
    param([string]$Path)

    $reader = [System.IO.StreamReader]::new($Path)
    try {
        $null = $reader.ReadLine()
        while (($line = $reader.ReadLine()) -ne $null) {
            if ($line.IndexOf(",VI,", [StringComparison]::Ordinal) -lt 0 -and
                $line.IndexOf(",GX FIFO,", [StringComparison]::Ordinal) -lt 0) {
                continue
            }

            $quoteStart = $line.IndexOf('"')
            if ($quoteStart -lt 0) {
                continue
            }

            $quoteEnd = $line.IndexOf('",', $quoteStart + 1, [StringComparison]::Ordinal)
            if ($quoteEnd -lt 0) {
                continue
            }

            $prefix = $line.Substring(0, $quoteStart).TrimEnd(',')
            $prefixFields = $prefix.Split(',')
            if ($prefixFields.Length -lt 3) {
                continue
            }

            $rest = $line.Substring($quoteEnd + 2).Split(',')
            if ($rest.Length -lt 5) {
                continue
            }

            [pscustomobject]@{
                instruction = $prefixFields[0]
                pc = $prefixFields[1]
                opcode = $prefixFields[2]
                disassembly = $line.Substring($quoteStart + 1, $quoteEnd - $quoteStart - 1).Replace('""', '"')
                device = $rest[0]
                kind = $rest[1]
                width = $rest[2]
                address = $rest[3]
                value = $rest[4]
            }
        }
    } finally {
        $reader.Dispose()
    }
}

$runRootPath = Resolve-FullPath $RunRoot
if ([string]::IsNullOrWhiteSpace($GxCopiesPath)) {
    $GxCopiesPath = Join-Path $runRootPath "gx-copies.csv"
} else {
    $GxCopiesPath = Resolve-FullPath $GxCopiesPath
}

if ([string]::IsNullOrWhiteSpace($MmioTracePath)) {
    $MmioTracePath = Join-Path $runRootPath "mmio.csv"
} else {
    $MmioTracePath = Resolve-FullPath $MmioTracePath
}

if ([string]::IsNullOrWhiteSpace($GxFrameSweepPath)) {
    $GxFrameSweepPath = Join-Path $runRootPath "gx-frame-sweep.csv"
} else {
    $GxFrameSweepPath = Resolve-FullPath $GxFrameSweepPath
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRootPath "vi-display-timeline"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

if (-not (Test-Path -LiteralPath $GxCopiesPath)) {
    throw "GX copy CSV not found: $GxCopiesPath"
}

$node = Get-Command node -ErrorAction SilentlyContinue
$nodeReportScript = Join-Path $PSScriptRoot "build-vi-display-timeline-report.js"
if ($null -ne $node -and (Test-Path -LiteralPath $MmioTracePath) -and (Test-Path -LiteralPath $nodeReportScript)) {
    & $node.Source $nodeReportScript `
        --run-root $runRootPath `
        --gx-copies $GxCopiesPath `
        --mmio $MmioTracePath `
        --gx-frame-sweep $GxFrameSweepPath `
        --out $OutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Node VI display timeline report failed with exit code $LASTEXITCODE"
    }

    return
}

$displayCopies = @(
    Import-Csv -LiteralPath $GxCopiesPath |
        ForEach-Object { Convert-CopyRow $_ } |
        Where-Object { $null -ne $_ -and $null -ne $_.fifo_offset }
) | Sort-Object fifo_offset

$viRows = [System.Collections.Generic.List[object]]::new()
$copyRows = [System.Collections.Generic.List[object]]::new()
$copyQueue = [System.Collections.Generic.Queue[object]]::new()
foreach ($copy in $displayCopies) {
    $copyQueue.Enqueue($copy)
}

$registerValues = @{}
$gxFifoBytes = 0L
$mmioAvailable = Test-Path -LiteralPath $MmioTracePath
if ($mmioAvailable) {
    $reader = [System.IO.StreamReader]::new($MmioTracePath)
    try {
        $null = $reader.ReadLine()
        while (($line = $reader.ReadLine()) -ne $null) {
            if ($line.IndexOf(",VI,", [StringComparison]::Ordinal) -lt 0 -and
                $line.IndexOf(",GX FIFO,", [StringComparison]::Ordinal) -lt 0) {
                continue
            }

            $quoteStart = $line.IndexOf('"')
            if ($quoteStart -lt 0) {
                continue
            }

            $quoteEnd = $line.IndexOf('",', $quoteStart + 1, [StringComparison]::Ordinal)
            if ($quoteEnd -lt 0) {
                continue
            }

            $prefix = $line.Substring(0, $quoteStart).TrimEnd(',')
            $prefixFields = $prefix.Split(',')
            if ($prefixFields.Length -lt 3) {
                continue
            }

            $rest = $line.Substring($quoteEnd + 2).Split(',')
            if ($rest.Length -lt 5) {
                continue
            }

            $instruction = Convert-ToNullableInt64 $prefixFields[0]
            $pc = $prefixFields[1]
            $opcode = $prefixFields[2]
            $disassembly = $line.Substring($quoteStart + 1, $quoteEnd - $quoteStart - 1).Replace('""', '"')
            $device = $rest[0]
            $kind = $rest[1]
            $width = Convert-ToNullableInt64 $rest[2]
            $address = Convert-ToNullableUInt32 $rest[3]
            $value = Convert-ToNullableUInt32 $rest[4]

            if ($device -eq "VI" -and $kind -eq "Write" -and $null -ne $address -and $null -ne $value) {
                $registerValues[(Format-Hex32 $address)] = Convert-ToUInt32Number $value
                $resolved = Get-ViResolvedAddresses $registerValues
                $viRows.Add([pscustomobject]@{
                    instruction = $instruction
                    pc = $pc
                    opcode = $opcode
                    disassembly = $disassembly
                    width = $width
                    address = Format-Hex32 $address
                    register = Get-ViRegisterName $address
                    value = Format-Hex32 $value
                    field0 = Format-Hex32 $resolved["field0"]
                    field1 = Format-Hex32 $resolved["field1"]
                    field2 = Format-Hex32 $resolved["field2"]
                    field3 = Format-Hex32 $resolved["field3"]
                }) | Out-Null
            }

            if ($device -eq "GX FIFO" -and $kind -eq "Write" -and $null -ne $width) {
                $gxFifoBytes += $width
                while ($copyQueue.Count -gt 0 -and $copyQueue.Peek().fifo_offset -lt $gxFifoBytes) {
                    $copy = $copyQueue.Dequeue()
                    $resolved = Get-ViResolvedAddresses $registerValues
                    $copy.instruction = $instruction
                    $copy.pc = $pc
                    $copy.opcode = $opcode
                    $copy.disassembly = $disassembly
                    $copy.vi_field0 = Format-Hex32 $resolved["field0"]
                    $copy.vi_field1 = Format-Hex32 $resolved["field1"]
                    $copy.vi_field2 = Format-Hex32 $resolved["field2"]
                    $copy.vi_field3 = Format-Hex32 $resolved["field3"]

                    foreach ($pairName in @("field0", "field1", "field2", "field3")) {
                        if ($null -ne $copy.display_address -and $resolved[$pairName] -eq $copy.display_address) {
                            $copy.vi_field = $pairName
                            break
                        }
                    }
                }

                if ($copyQueue.Count -eq 0) {
                    break
                }
            }
        }
    } finally {
        $reader.Dispose()
    }
}

foreach ($copy in $displayCopies) {
    $copyRows.Add([pscustomobject]@{
        copy_index = $copy.copy_index
        fifo_offset = $copy.fifo_offset_text
        draws_seen = $copy.draws_seen
        instruction = $copy.instruction
        pc = $copy.pc
        display_address = $copy.display_address_text
        display_nonblack = $copy.display_nonblack
        display_nonblack_percent = $copy.display_nonblack_percent
        display_nonblack_bounds = $copy.display_nonblack_bounds
        vi_field = $copy.vi_field
        vi_field0 = $copy.vi_field0
        vi_field1 = $copy.vi_field1
        vi_field2 = $copy.vi_field2
        vi_field3 = $copy.vi_field3
        source_width = $copy.source_width
        source_height = $copy.source_height
        display_width = $copy.display_width
        display_height = $copy.display_height
        clear = $copy.clear
    }) | Out-Null
}

$selectedRows = @()
if (Test-Path -LiteralPath $GxFrameSweepPath) {
    $selectedRows = @(
        Import-Csv -LiteralPath $GxFrameSweepPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace((Get-ObjectValue $_ "selected_copy_index")) } |
            ForEach-Object {
                $row = $_
                $selectedAddress = Convert-ToNullableUInt32 (Get-ObjectValue $_ "selected_copy_destination_address")
                $selectedDrawsSeen = Get-ObjectValue $row "selected_copy_draws_seen"
                $matchedCopy = $copyRows |
                    Where-Object { $_.display_address -eq (Format-Hex32 $selectedAddress) -and "$($_.draws_seen)" -eq "$selectedDrawsSeen" } |
                    Select-Object -First 1
                [pscustomobject]@{
                    skip = Get-ObjectValue $row "skip"
                    path = Get-ObjectValue $row "path"
                    source = Get-ObjectValue $row "source"
                    source_copy_index = Get-ObjectValue $row "source_copy_index"
                    selected_copy_index = Get-ObjectValue $row "selected_copy_index"
                    selected_copy_kind = Get-ObjectValue $row "selected_copy_kind"
                    selected_copy_draws_seen = $selectedDrawsSeen
                    selected_copy_fifo_offset = Get-ObjectValue $row "selected_copy_fifo_offset"
                    selected_copy_destination_address = Format-Hex32 $selectedAddress
                    lifecycle_phase = Get-ObjectValue $row "lifecycle_phase"
                    matched_display_copy = if ($null -ne $matchedCopy) { $matchedCopy.copy_index } else { "" }
                }
            }
    )
}

$viWritePath = Join-Path $OutputDirectory "vi-register-writes.csv"
$displayJoinPath = Join-Path $OutputDirectory "display-copy-vi-join.csv"
$selectedPath = Join-Path $OutputDirectory "selected-frame-copy-join.csv"
$reportPath = Join-Path $OutputDirectory "vi-display-timeline-report.json"

$viRows | Export-Csv -LiteralPath $viWritePath -NoTypeInformation
$copyRows | Export-Csv -LiteralPath $displayJoinPath -NoTypeInformation
$selectedRows | Export-Csv -LiteralPath $selectedPath -NoTypeInformation

$report = [pscustomobject]@{
    schema = "ngcsharp.vi-display-timeline.v1"
    runRoot = $runRootPath
    gxCopiesPath = $GxCopiesPath
    mmioTracePath = if ($mmioAvailable) { $MmioTracePath } else { $null }
    gxFrameSweepPath = if (Test-Path -LiteralPath $GxFrameSweepPath) { $GxFrameSweepPath } else { $null }
    displayCopies = $displayCopies.Count
    displayCopiesWithInstruction = @($copyRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.instruction) }).Count
    displayCopiesWithViFieldMatch = @($copyRows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.vi_field) }).Count
    viWrites = $viRows.Count
    selectedFrameRows = $selectedRows.Count
    viRegisterWritesCsvPath = $viWritePath
    displayCopyViJoinCsvPath = $displayJoinPath
    selectedFrameCopyJoinCsvPath = $selectedPath
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath

Write-Host "VI display timeline report: $reportPath"
$copyRows | Select-Object copy_index,fifo_offset,draws_seen,instruction,display_address,display_nonblack_percent,vi_field,vi_field0,vi_field1,vi_field2,vi_field3 | Format-Table -AutoSize
