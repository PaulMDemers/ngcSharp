param(
    [Parameter(Mandatory = $true)]
    [string]$FifoCsvPath,
    [string]$StreamRecordCsvPath = "",
    [string]$VertexCsvPath = "",
    [string]$JsonPath = "",
    [int]$Top = 16,
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

function Convert-HexInt64 {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [int64]0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToInt64($text.Substring(2), 16)
    }

    return [Convert]::ToInt64($text, 10)
}

function Convert-BeUInt32ToFloat {
    param([uint32]$Value)

    $bytes = [byte[]]@(
        [byte](($Value -shr 24) -band 0xFF),
        [byte](($Value -shr 16) -band 0xFF),
        [byte](($Value -shr 8) -band 0xFF),
        [byte]($Value -band 0xFF)
    )

    if ([BitConverter]::IsLittleEndian) {
        [Array]::Reverse($bytes)
    }

    return [BitConverter]::ToSingle($bytes, 0)
}

function Format-Float {
    param([single]$Value)

    if ([single]::IsNaN($Value) -or [single]::IsInfinity($Value)) {
        return $Value.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $Value.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Hex32 {
    param([uint32]$Value)

    return "0x{0:X8}" -f $Value
}

function Normalize-HexWord {
    param([string]$Value)

    return Format-Hex32 (Convert-HexUInt32 $Value)
}

$fifoFullPath = Resolve-FullPath $FifoCsvPath
if (-not (Test-Path -LiteralPath $fifoFullPath)) {
    throw "GX FIFO trace not found: $fifoFullPath"
}

$rows = @(Import-Csv -LiteralPath $fifoFullPath)
if ($rows.Count -eq 0) {
    throw "GX FIFO trace has no rows: $fifoFullPath"
}

$decoded = @(
    $rows | ForEach-Object {
        $width = [int]$_.width
        $value = Convert-HexUInt32 $_.value
        $pc = $_.pc
        $semantic = if ($width -eq 4 -and $_.disassembly -like "stfs*") {
            "float"
        } elseif ($width -eq 2) {
            "half"
        } elseif ($width -eq 4) {
            "word"
        } else {
            "byte"
        }

        [pscustomobject][ordered]@{
            instruction = [int64]$_.instruction
            pc = $pc
            disassembly = $_.disassembly
            fifoStart = Convert-HexInt64 $_.fifo_offset_start
            fifoEnd = Convert-HexInt64 $_.fifo_offset_end
            width = $width
            value = Format-Hex32 $value
            semantic = $semantic
            float = if ($semantic -eq "float") { Format-Float (Convert-BeUInt32ToFloat $value) } else { "" }
        }
    }
)

$pcGroups = @(
    $decoded |
        Group-Object pc,disassembly,width,semantic |
        Sort-Object Count -Descending |
        Select-Object -First $Top |
        ForEach-Object {
            $first = $_.Group | Select-Object -First 1
            [pscustomobject][ordered]@{
                count = $_.Count
                pc = $first.pc
                width = $first.width
                semantic = $first.semantic
                disassembly = $first.disassembly
                firstFifoStart = "0x{0:X}" -f $first.fifoStart
                firstValue = $first.value
                firstFloat = $first.float
            }
        }
)

$semanticGroups = @(
    $decoded |
        Group-Object semantic,width |
        Sort-Object Count -Descending |
        ForEach-Object {
            $first = $_.Group | Select-Object -First 1
            [pscustomobject][ordered]@{
                count = $_.Count
                semantic = $first.semantic
                width = $first.width
            }
        }
)

$valueOverlap = @()
if (-not [string]::IsNullOrWhiteSpace($StreamRecordCsvPath)) {
    $streamRecordFullPath = Resolve-FullPath $StreamRecordCsvPath
    if (-not (Test-Path -LiteralPath $streamRecordFullPath)) {
        throw "Sonic stream record CSV not found: $streamRecordFullPath"
    }

    $streamRows = @(Import-Csv -LiteralPath $streamRecordFullPath)
    $streamByWord = @{}
    foreach ($row in $streamRows) {
        $word = Normalize-HexWord $row.word
        if (-not $streamByWord.ContainsKey($word)) {
            $streamByWord[$word] = [System.Collections.Generic.List[object]]::new()
        }

        $streamByWord[$word].Add($row)
    }

    $trivialWords = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($word in @("0x00000000", "0xFFFFFFFF", "0xFF000000")) {
        $null = $trivialWords.Add($word)
    }

    $valueOverlap = @(
        $decoded |
            Where-Object { -not $trivialWords.Contains($_.value) -and $streamByWord.ContainsKey($_.value) } |
            Select-Object -First $Top |
            ForEach-Object {
                $match = $streamByWord[$_.value][0]
                [pscustomobject][ordered]@{
                    fifoInstruction = $_.instruction
                    fifoOffset = "0x{0:X}" -f $_.fifoStart
                    pc = $_.pc
                    semantic = $_.semantic
                    value = $_.value
                    float = $_.float
                    packet = $match.packet
                    kind = $match.kind
                    recordIndex = $match.recordIndex
                    stream0Offset = $match.stream0Offset
                }
            }
    )
}

function Try-GetNextRow {
    param(
        [object[]]$Rows,
        [int]$Index,
        [string]$Pc
    )

    if ($Index -ge $Rows.Count) {
        return $null
    }

    $row = $Rows[$Index]
    if ($row.pc -ne $Pc) {
        return $null
    }

    return $row
}

$candidateVertices = @()
for ($i = 0; $i -lt $decoded.Count; $i++) {
    $row = $decoded[$i]
    if ($row.pc -ne "0x80120148") {
        continue
    }

    $y = Try-GetNextRow $decoded ($i + 1) "0x8012014C"
    $z = Try-GetNextRow $decoded ($i + 2) "0x80120150"
    if ($null -eq $y -or $null -eq $z) {
        continue
    }

    $color = $null
    $attr0 = $null
    $attr1 = $null
    for ($j = $i + 3; $j -lt [Math]::Min($i + 8, $decoded.Count); $j++) {
        if ($decoded[$j].pc -eq "0x8012013C" -and $null -eq $color) {
            $color = $decoded[$j]
        } elseif ($decoded[$j].pc -eq "0x8012012C" -and $null -eq $attr0) {
            $attr0 = $decoded[$j]
        } elseif ($decoded[$j].pc -eq "0x80120130" -and $null -eq $attr1) {
            $attr1 = $decoded[$j]
        }
    }

    $candidateVertices += [pscustomobject][ordered]@{
        instruction = $row.instruction
        fifoOffset = "0x{0:X}" -f $row.fifoStart
        xWord = $row.value
        yWord = $y.value
        zWord = $z.value
        x = $row.float
        y = $y.float
        z = $z.float
        color = if ($null -ne $color) { $color.value } else { "" }
        attr0 = if ($null -ne $attr0) { $attr0.value } else { "" }
        attr1 = if ($null -ne $attr1) { $attr1.value } else { "" }
    }
}

if (-not [string]::IsNullOrWhiteSpace($VertexCsvPath)) {
    $vertexCsvFullPath = Resolve-FullPath $VertexCsvPath
    $directory = Split-Path -Parent $vertexCsvFullPath
    if (-not [string]::IsNullOrEmpty($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $candidateVertices | Export-Csv -LiteralPath $vertexCsvFullPath -NoTypeInformation
}

$summaryObject = [ordered]@{
    fifoCsvPath = $fifoFullPath
    rows = $rows.Count
    firstInstruction = ($decoded | Select-Object -First 1).instruction
    lastInstruction = ($decoded | Select-Object -Last 1).instruction
    firstFifoOffset = "0x{0:X}" -f (($decoded | Select-Object -First 1).fifoStart)
    lastFifoOffset = "0x{0:X}" -f (($decoded | Select-Object -Last 1).fifoEnd)
    semanticGroups = @($semanticGroups)
    pcGroups = @($pcGroups)
    streamValueOverlap = @($valueOverlap)
    candidateVertexCount = @($candidateVertices).Count
    sampleVertices = @($candidateVertices | Select-Object -First $Top)
}

if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
    $jsonFullPath = Resolve-FullPath $JsonPath
    $directory = Split-Path -Parent $jsonFullPath
    if (-not [string]::IsNullOrEmpty($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $summaryObject | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonFullPath -Encoding UTF8
}

if ($PassThru) {
    [pscustomobject]$summaryObject
} else {
    [pscustomobject]@{
        path = $fifoFullPath
        rows = $rows.Count
        firstInstruction = $summaryObject.firstInstruction
        lastInstruction = $summaryObject.lastInstruction
        fifoRange = "$($summaryObject.firstFifoOffset)..$($summaryObject.lastFifoOffset)"
        streamValueOverlapRows = @($valueOverlap).Count
        candidateVertices = @($candidateVertices).Count
    } | Format-List

    Write-Host ""
    Write-Host "Write semantics:"
    $semanticGroups | Format-Table -AutoSize

    Write-Host ""
    Write-Host "Top writer PCs:"
    $pcGroups | Format-Table -AutoSize

    if (@($valueOverlap).Count -gt 0) {
        Write-Host ""
        Write-Host "FIFO values that also appear in stream0 records:"
        $valueOverlap | Format-Table -AutoSize
    }

    if (@($candidateVertices).Count -gt 0) {
        Write-Host ""
        Write-Host "First candidate vertices from 0x80120148 emitter:"
        $candidateVertices | Select-Object -First $Top | Format-Table -AutoSize
    }
}
