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
        return "0x{0:X}" -f ([int64]::Parse($trimmed.Substring(3), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "0x{0:X}" -f ([int64]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    return "0x{0:X}" -f ([int64]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture))
}

function Convert-ToNullableInt {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [int]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToNullableDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Unique {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique) -join " ")
}

function Format-Range {
    param($Values)

    $numbers = @($Values | Where-Object { $null -ne $_ })
    if ($numbers.Count -eq 0) {
        return ""
    }

    $measure = $numbers | Measure-Object -Minimum -Maximum
    return ("{0:0.###}..{1:0.###}" -f [double]$measure.Minimum, [double]$measure.Maximum)
}

function Format-HexRange {
    param($Values)

    $items = @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
    if ($items.Count -eq 0) {
        return ""
    }

    return "$($items[0])..$($items[-1])"
}

function Get-VertexKey {
    param(
        [object]$Draw,
        [object]$Vertex
    )

    return "$Draw/$Vertex"
}

function Get-DrawTextureKey {
    param(
        [object]$Draw,
        [string]$Texture
    )

    return "$Draw/$Texture"
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-packet-material-partitions"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$packetTimelineCsvPath = Join-Path $runRoot "sonic-packet-timeline.csv"
$coverageCsvPath = Join-Path $runRoot "gx-coverage.csv"
$triangleSummaryCsvPath = Join-Path $runRoot "gx-triangle-coverage.summary.csv"
$vertexCsvPath = Join-Path $runRoot "gx-vertices.csv"
$sourceMapCsvPath = Join-Path $runRoot "sonic-transform-source-map.csv"
$stateTimelineCsvPath = Join-Path $runRoot "gx-state-timeline.csv"

foreach ($required in @($packetTimelineCsvPath, $coverageCsvPath, $triangleSummaryCsvPath, $vertexCsvPath, $sourceMapCsvPath)) {
    if (-not (Test-CsvHasRows $required)) {
        throw "Required CSV missing or empty: $required"
    }
}

$focusPacket = Normalize-Hex $FocusPacket
$packetRows = @(
    Import-Csv -LiteralPath $packetTimelineCsvPath |
        Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket -and -not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $_ "mapped_draw_start")) }
)
if ($packetRows.Count -eq 0) {
    throw "No mapped packet timeline rows found for packet $focusPacket in $packetTimelineCsvPath"
}

$packetRow = $packetRows | Select-Object -First 1
$drawStart = Convert-ToNullableInt (Get-ObjectValue $packetRow "mapped_draw_start")
$drawEnd = Convert-ToNullableInt (Get-ObjectValue $packetRow "mapped_draw_end")
if ($null -eq $drawStart -or $null -eq $drawEnd -or $drawEnd -lt $drawStart) {
    throw "Packet $focusPacket has an invalid mapped draw range."
}

$coverageRows = @(Import-Csv -LiteralPath $coverageCsvPath)
$triangleRows = @(Import-Csv -LiteralPath $triangleSummaryCsvPath)
$vertexRows = @(Import-Csv -LiteralPath $vertexCsvPath)
$sourceRows = @(Import-Csv -LiteralPath $sourceMapCsvPath)
$stateRows = if (Test-CsvHasRows $stateTimelineCsvPath) { @(Import-Csv -LiteralPath $stateTimelineCsvPath) } else { @() }

$coverageByDraw = @{}
foreach ($row in $coverageRows) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $row "draw_index")
    if ($null -ne $draw) {
        $coverageByDraw[$draw] = $row
    }
}

$stateByDraw = @{}
foreach ($row in $stateRows) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $row "draws_seen")
    if ($null -ne $draw -and [string](Get-ObjectValue $row "event") -eq "draw") {
        $stateByDraw[$draw] = $row
    }
}

$vertexByDrawIndex = @{}
$verticesByDraw = @{}
foreach ($vertex in $vertexRows) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $vertex "draw_index")
    $vertexByDrawIndex[(Get-VertexKey (Get-ObjectValue $vertex "draw_index") (Get-ObjectValue $vertex "vertex_index"))] = $vertex
    if ($null -ne $draw) {
        if (-not $verticesByDraw.ContainsKey($draw)) {
            $verticesByDraw[$draw] = New-Object System.Collections.Generic.List[object]
        }

        $verticesByDraw[$draw].Add($vertex)
    }
}

$sourceByFifo = @{}
foreach ($row in $sourceRows) {
    $sourceByFifo[(Normalize-Hex ([string](Get-ObjectValue $row "gx_fifo_offset")))] = $row
}

$trianglesByDraw = @{}
foreach ($triangle in $triangleRows) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $triangle "draw_index")
    if ($null -eq $draw -or $draw -lt $drawStart -or $draw -gt $drawEnd) {
        continue
    }

    if (-not $trianglesByDraw.ContainsKey($draw)) {
        $trianglesByDraw[$draw] = New-Object System.Collections.Generic.List[object]
    }

    $trianglesByDraw[$draw].Add($triangle)
}

$partitionRows = New-Object System.Collections.Generic.List[object]
for ($draw = $drawStart; $draw -le $drawEnd; $draw++) {
    $coverage = if ($coverageByDraw.ContainsKey($draw)) { $coverageByDraw[$draw] } else { $null }
    $state = if ($stateByDraw.ContainsKey($draw)) { $stateByDraw[$draw] } else { $null }
    $drawTriangles = if ($trianglesByDraw.ContainsKey($draw)) { @($trianglesByDraw[$draw].ToArray()) } else { @() }
    $textureGroups = @($drawTriangles | Group-Object { Normalize-Hex ([string](Get-ObjectValue $_ "texture_address")) })

    if ($textureGroups.Count -eq 0) {
        $textureGroups = @([pscustomobject]@{ Name = ""; Group = @() })
    }

    foreach ($textureGroup in $textureGroups) {
        $texture = $textureGroup.Name
        $rows = @($textureGroup.Group)
        $sourceRecords = New-Object System.Collections.Generic.List[string]
        $outputIndices = New-Object System.Collections.Generic.List[string]
        $inputIndices = New-Object System.Collections.Generic.List[string]
        $inputAddresses = New-Object System.Collections.Generic.List[string]
        $sourceXs = New-Object System.Collections.Generic.List[object]
        $sourceYs = New-Object System.Collections.Generic.List[object]
        $sourceZs = New-Object System.Collections.Generic.List[object]
        $texSs = New-Object System.Collections.Generic.List[object]
        $texTs = New-Object System.Collections.Generic.List[object]
        $vertexKeys = New-Object System.Collections.Generic.List[string]

        if ($rows.Count -eq 0 -and $verticesByDraw.ContainsKey($draw)) {
            foreach ($vertex in @($verticesByDraw[$draw].ToArray())) {
                $vertexKey = Get-VertexKey $draw (Get-ObjectValue $vertex "vertex_index")
                if ($null -eq $vertex) {
                    continue
                }

                $vertexKeys.Add($vertexKey)
                $texSs.Add((Convert-ToNullableDouble (Get-ObjectValue $vertex "tex0_s")))
                $texTs.Add((Convert-ToNullableDouble (Get-ObjectValue $vertex "tex0_t")))
                $fifo = Normalize-Hex ([string](Get-ObjectValue $vertex "vertex_payload_offset"))
                $source = if ($sourceByFifo.ContainsKey($fifo)) { $sourceByFifo[$fifo] } else { $null }
                if ($null -eq $source) {
                    continue
                }

                $sourceRecords.Add([string](Get-ObjectValue $source "source_record"))
                $outputIndices.Add([string](Get-ObjectValue $source "output_index"))
                $inputIndices.Add([string](Get-ObjectValue $source "input_index"))
                $inputAddresses.Add([string](Get-ObjectValue $source "input_address"))
                $sourceXs.Add((Convert-ToNullableDouble (Get-ObjectValue $source "source_x")))
                $sourceYs.Add((Convert-ToNullableDouble (Get-ObjectValue $source "source_y")))
                $sourceZs.Add((Convert-ToNullableDouble (Get-ObjectValue $source "source_z")))
            }
        } else {
            foreach ($triangle in $rows) {
                foreach ($slot in @("a", "b", "c")) {
                    $vertexIndex = Get-ObjectValue $triangle "vertex_$slot"
                    $vertexKey = Get-VertexKey $draw $vertexIndex
                    $vertex = $vertexByDrawIndex[$vertexKey]
                    if ($null -eq $vertex) {
                        continue
                    }

                    $vertexKeys.Add($vertexKey)
                    $texSs.Add((Convert-ToNullableDouble (Get-ObjectValue $vertex "tex0_s")))
                    $texTs.Add((Convert-ToNullableDouble (Get-ObjectValue $vertex "tex0_t")))
                    $fifo = Normalize-Hex ([string](Get-ObjectValue $vertex "vertex_payload_offset"))
                    $source = if ($sourceByFifo.ContainsKey($fifo)) { $sourceByFifo[$fifo] } else { $null }
                    if ($null -eq $source) {
                        continue
                    }

                    $sourceRecords.Add([string](Get-ObjectValue $source "source_record"))
                    $outputIndices.Add([string](Get-ObjectValue $source "output_index"))
                    $inputIndices.Add([string](Get-ObjectValue $source "input_index"))
                    $inputAddresses.Add([string](Get-ObjectValue $source "input_address"))
                    $sourceXs.Add((Convert-ToNullableDouble (Get-ObjectValue $source "source_x")))
                    $sourceYs.Add((Convert-ToNullableDouble (Get-ObjectValue $source "source_y")))
                    $sourceZs.Add((Convert-ToNullableDouble (Get-ObjectValue $source "source_z")))
                }
            }
        }

        $coveredPixels = @($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "covered_pixels") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
        $colorWrites = @($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "color_writes") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
        $blackWrites = @($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "black_color_writes") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
        $blackRatio = if ($colorWrites -gt 0) { "{0:0.######}" -f ($blackWrites / $colorWrites) } else { "" }

        $partitionRows.Add([pscustomobject][ordered]@{
            packet = Normalize-Hex ([string](Get-ObjectValue $packetRow "packet"))
            packet_kind = Normalize-Hex ([string](Get-ObjectValue $packetRow "packet_kind"))
            object = Normalize-Hex ([string](Get-ObjectValue $packetRow "object"))
            object_kind = Get-ObjectValue $packetRow "object_kind"
            stream0 = Normalize-Hex ([string](Get-ObjectValue $packetRow "stream0"))
            stream1 = Normalize-Hex ([string](Get-ObjectValue $packetRow "stream1"))
            object_xyz = Get-ObjectValue $packetRow "object_xyz"
            matrix_translation = Get-ObjectValue $packetRow "matrix_translation"
            packet_draw_start = $drawStart
            packet_draw_end = $drawEnd
            draw_index = $draw
            draw_delta = $draw - $drawStart
            draw_fifo_offset = Get-ObjectValue $coverage "fifo_offset" (Get-ObjectValue $state "fifo_offset")
            primitive = Get-ObjectValue $coverage "primitive" (Get-ObjectValue $state "primitive")
            format = Get-ObjectValue $coverage "format" (Get-ObjectValue $state "format")
            draw_vertices = Get-ObjectValue $coverage "vertices" (Get-ObjectValue $state "vertices")
            projected_vertices = Get-ObjectValue $coverage "projected_vertices"
            clipped_vertices = Get-ObjectValue $coverage "clipped_vertices"
            draw_covered_pixels = Get-ObjectValue $coverage "covered_pixels"
            draw_color_writes = Get-ObjectValue $coverage "color_writes"
            draw_black_color_writes = Get-ObjectValue $coverage "black_color_writes"
            draw_degenerate_triangles = Get-ObjectValue $coverage "degenerate_triangles_delta"
            state_gen_mode = Get-ObjectValue $state "gen_mode"
            state_z_mode = Get-ObjectValue $state "z_mode"
            state_blend_mode = Get-ObjectValue $state "blend_mode"
            state_alpha_compare = Get-ObjectValue $state "alpha_compare"
            texture_address = $texture
            texture_format = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "texture_format" })
            texture_size = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "texture_size" })
            texture_filter = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "texture_filter" })
            texture_lod = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "texture_lod" })
            stage0_mode = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "stage0_mode" })
            rendered_triangle_count = $rows.Count
            rendered_vertex_refs = $vertexKeys.Count
            unique_rendered_vertices = @($vertexKeys | Sort-Object -Unique).Count
            covered_pixels = if ($rows.Count -gt 0) { [int64]$coveredPixels } else { "" }
            color_writes = if ($rows.Count -gt 0) { [int64]$colorWrites } else { "" }
            black_color_writes = if ($rows.Count -gt 0) { [int64]$blackWrites } else { "" }
            black_write_ratio = $blackRatio
            source_record_count = @($sourceRecords | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique).Count
            source_records = Format-Unique $sourceRecords
            source_record_range = Format-HexRange $sourceRecords
            output_indices = Format-Unique $outputIndices
            input_indices = Format-Unique $inputIndices
            input_address_range = Format-HexRange $inputAddresses
            source_x_range = Format-Range $sourceXs
            source_y_range = Format-Range $sourceYs
            source_z_range = Format-Range $sourceZs
            tex_s_range = Format-Range $texSs
            tex_t_range = Format-Range $texTs
            triangle_indices = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "triangle_index" })
            texture_xy = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "texture_xy" })
            sample_raster_rgba = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "sample_raster_rgba" })
            sample_tev_rgba = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "sample_tev_rgba" })
            partition_kind = if ($rows.Count -eq 0) { "no-rendered-material" } elseif ($blackRatio -ne "" -and [double]::Parse($blackRatio, [System.Globalization.CultureInfo]::InvariantCulture) -ge 0.5) { "dark-material" } else { "lit-material" }
        })
    }
}

$sequenceRows = New-Object System.Collections.Generic.List[object]
$sequenceIndex = 0
$active = $null
foreach ($row in @($partitionRows.ToArray() | Sort-Object {[int](Get-ObjectValue $_ "draw_index")}, texture_address)) {
    $signature = "{0}|{1}|{2}" -f (Get-ObjectValue $row "partition_kind"), (Get-ObjectValue $row "texture_address"), (Get-ObjectValue $row "texture_size")
    if ($null -eq $active -or $active.Signature -ne $signature) {
        $sequenceIndex++
        $active = [pscustomobject]@{
            Signature = $signature
            Rows = New-Object System.Collections.Generic.List[object]
        }
        $sequenceRows.Add($active)
    }

    $active.Rows.Add($row)
}

$sequenceOutputRows = @(
    foreach ($sequence in $sequenceRows) {
        $rows = @($sequence.Rows.ToArray())
        $draws = @($rows | ForEach-Object { [int](Get-ObjectValue $_ "draw_index") } | Sort-Object -Unique)
        [pscustomobject][ordered]@{
            sequence_index = [array]::IndexOf($sequenceRows.ToArray(), $sequence) + 1
            partition_kind = Get-ObjectValue $rows[0] "partition_kind"
            texture_address = Get-ObjectValue $rows[0] "texture_address"
            texture_size = Get-ObjectValue $rows[0] "texture_size"
            draw_start = $draws[0]
            draw_end = $draws[-1]
            draw_count = $draws.Count
            draws = $draws -join " "
            total_triangles = @($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "rendered_triangle_count") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
            total_color_writes = @($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "color_writes") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
            total_black_color_writes = @($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "black_color_writes") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
            source_records = Format-Unique ($rows | ForEach-Object { [string](Get-ObjectValue $_ "source_records") -split " " })
            output_indices = Format-Unique ($rows | ForEach-Object { [string](Get-ObjectValue $_ "output_indices") -split " " })
            tex_s_range = Format-Range ($rows | ForEach-Object { [string](Get-ObjectValue $_ "tex_s_range") -split "\.\." | ForEach-Object { Convert-ToNullableDouble $_ } })
            tex_t_range = Format-Range ($rows | ForEach-Object { [string](Get-ObjectValue $_ "tex_t_range") -split "\.\." | ForEach-Object { Convert-ToNullableDouble $_ } })
        }
    }
)

$partitionCsvPath = Join-Path $OutputDirectory "packet-material-partitions.csv"
$sequenceCsvPath = Join-Path $OutputDirectory "packet-material-sequences.csv"
$jsonPath = Join-Path $OutputDirectory "packet-material-partition-report.json"

$partitionRows | Export-Csv -LiteralPath $partitionCsvPath -NoTypeInformation -Encoding UTF8
$sequenceOutputRows | Export-Csv -LiteralPath $sequenceCsvPath -NoTypeInformation -Encoding UTF8

$report = [pscustomobject]([ordered]@{
    run_directory = $runRoot
    focus_packet = $focusPacket
    packet = $packetRow
    draw_start = $drawStart
    draw_end = $drawEnd
    partitions = [object[]]$partitionRows.ToArray()
    sequences = [object[]]$sequenceOutputRows
})

$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic packet material partition report: $jsonPath"
Write-Output $sequenceOutputRows
