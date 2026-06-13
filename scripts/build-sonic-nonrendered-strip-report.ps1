param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0",
    [int]$FrameWidth = 640,
    [int]$FrameHeight = 480
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

function Format-Range {
    param($Values)

    $numbers = @($Values | Where-Object { $null -ne $_ })
    if ($numbers.Count -eq 0) {
        return ""
    }

    $measure = $numbers | Measure-Object -Minimum -Maximum
    return ("{0:0.###}..{1:0.###}" -f [double]$measure.Minimum, [double]$measure.Maximum)
}

function Format-Unique {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique) -join " ")
}

function Add-Reason {
    param(
        [System.Collections.Generic.List[string]]$Reasons,
        [string]$Reason
    )

    if (-not [string]::IsNullOrWhiteSpace($Reason) -and -not $Reasons.Contains($Reason)) {
        $Reasons.Add($Reason)
    }
}

function Get-DrawReason {
    param(
        [object]$Coverage,
        [object]$VertexSummary,
        [int]$Width,
        [int]$Height
    )

    $reasons = New-Object System.Collections.Generic.List[string]
    $vertices = Convert-ToNullableInt (Get-ObjectValue $Coverage "vertices")
    $projected = Convert-ToNullableInt (Get-ObjectValue $Coverage "projected_vertices")
    $clipped = Convert-ToNullableInt (Get-ObjectValue $Coverage "clipped_vertices")
    $covered = Convert-ToNullableInt (Get-ObjectValue $Coverage "covered_pixels")
    $colorWrites = Convert-ToNullableInt (Get-ObjectValue $Coverage "color_writes")
    $depthRejected = Convert-ToNullableInt (Get-ObjectValue $Coverage "depth_rejected")
    $alphaRejected = Convert-ToNullableInt (Get-ObjectValue $Coverage "alpha_rejected")
    $nearCulled = Convert-ToNullableInt (Get-ObjectValue $Coverage "near_clip_culled_triangles")
    $nearOutput = Convert-ToNullableInt (Get-ObjectValue $Coverage "near_clip_output_triangles")
    $clipInputs = Convert-ToNullableInt (Get-ObjectValue $Coverage "clip_input_triangles")
    $degenerate = Convert-ToNullableInt (Get-ObjectValue $Coverage "degenerate_triangles_delta")
    $rasterBefore = Convert-ToNullableInt (Get-ObjectValue $Coverage "raster_before")

    if ($colorWrites -gt 0) {
        Add-Reason $reasons "rendered-color"
    }

    if ($covered -gt 0 -and $colorWrites -le 0) {
        Add-Reason $reasons "covered-no-color-write"
    }

    if ($depthRejected -gt 0 -and $colorWrites -le 0) {
        Add-Reason $reasons "depth-rejected"
    }

    if ($alphaRejected -gt 0 -and $colorWrites -le 0) {
        Add-Reason $reasons "alpha-rejected"
    }

    if ($rasterBefore -le 0) {
        Add-Reason $reasons "raster-budget-exhausted"
    }

    if ($vertices -gt 0 -and $clipped -ge $vertices) {
        Add-Reason $reasons "all-vertices-clipped"
    } elseif ($projected -le 0 -and $clipInputs -gt 0) {
        Add-Reason $reasons "all-clip-inputs-culled"
    } elseif ($nearCulled -gt 0 -and $nearOutput -le 0) {
        Add-Reason $reasons "near-clip-culled"
    } elseif ($nearCulled -gt 0 -or $clipped -gt 0) {
        Add-Reason $reasons "partially-clipped"
    }

    $minX = Convert-ToNullableDouble (Get-ObjectValue $VertexSummary "screen_min_x")
    $maxX = Convert-ToNullableDouble (Get-ObjectValue $VertexSummary "screen_max_x")
    $minY = Convert-ToNullableDouble (Get-ObjectValue $VertexSummary "screen_min_y")
    $maxY = Convert-ToNullableDouble (Get-ObjectValue $VertexSummary "screen_max_y")
    if ($null -ne $minX -and $null -ne $maxX -and $null -ne $minY -and $null -ne $maxY) {
        if ($maxX -lt 0) {
            Add-Reason $reasons "offscreen-left"
        } elseif ($minX -ge $Width) {
            Add-Reason $reasons "offscreen-right"
        }

        if ($maxY -lt 0) {
            Add-Reason $reasons "offscreen-above"
        } elseif ($minY -ge $Height) {
            Add-Reason $reasons "offscreen-below"
        }
    }

    if ($degenerate -gt 0 -and $colorWrites -le 0) {
        Add-Reason $reasons "degenerate-or-offscreen-triangles"
    }

    if ($reasons.Count -eq 0) {
        Add-Reason $reasons "unknown-no-coverage"
    }

    return $reasons -join "|"
}

function Get-PrimaryReason {
    param([string]$Reason)

    $tags = @([string]$Reason -split "\|" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($tags -contains "rendered-color") {
        return "rendered"
    }

    if ($tags -contains "raster-budget-exhausted") {
        return "raster-budget-exhausted"
    }

    if ($tags -contains "depth-rejected") {
        return "depth-rejected"
    }

    if ($tags -contains "alpha-rejected") {
        return "alpha-rejected"
    }

    if ($tags -contains "all-vertices-clipped" -or $tags -contains "all-clip-inputs-culled" -or $tags -contains "near-clip-culled") {
        return "fully-clipped"
    }

    foreach ($offscreenTag in @("offscreen-left", "offscreen-right", "offscreen-above", "offscreen-below")) {
        if ($tags -contains $offscreenTag) {
            return $offscreenTag
        }
    }

    if ($tags -contains "partially-clipped") {
        return "partially-clipped"
    }

    if ($tags -contains "degenerate-or-offscreen-triangles") {
        return "degenerate-or-offscreen"
    }

    return "unknown-no-coverage"
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-nonrendered-strips"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$packetTimelineCsvPath = Join-Path $runRoot "sonic-packet-timeline.csv"
$coverageCsvPath = Join-Path $runRoot "gx-coverage.csv"
$vertexSummaryCsvPath = Join-Path $runRoot "gx-vertices.summary.csv"
$partitionCsvPath = Join-Path $runRoot "sonic-packet-material-partitions\packet-material-partitions.csv"

foreach ($required in @($packetTimelineCsvPath, $coverageCsvPath, $vertexSummaryCsvPath, $partitionCsvPath)) {
    if (-not (Test-CsvHasRows $required)) {
        throw "Required CSV missing or empty: $required"
    }
}

$focusPacket = Normalize-Hex $FocusPacket
$packetRow = @(
    Import-Csv -LiteralPath $packetTimelineCsvPath |
        Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket -and -not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $_ "mapped_draw_start")) } |
        Select-Object -First 1
)
if ($packetRow.Count -eq 0) {
    throw "No mapped packet timeline rows found for packet $focusPacket."
}

$drawStart = Convert-ToNullableInt (Get-ObjectValue $packetRow[0] "mapped_draw_start")
$drawEnd = Convert-ToNullableInt (Get-ObjectValue $packetRow[0] "mapped_draw_end")
if ($null -eq $drawStart -or $null -eq $drawEnd -or $drawEnd -lt $drawStart) {
    throw "Packet $focusPacket has an invalid mapped draw range."
}

$coverageByDraw = @{}
foreach ($row in (Import-Csv -LiteralPath $coverageCsvPath)) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $row "draw_index")
    if ($null -ne $draw) {
        $coverageByDraw[$draw] = $row
    }
}

$vertexSummaryByDraw = @{}
foreach ($row in (Import-Csv -LiteralPath $vertexSummaryCsvPath)) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $row "draw_index")
    if ($null -ne $draw) {
        $vertexSummaryByDraw[$draw] = $row
    }
}

$partitionByDraw = @{}
foreach ($row in (Import-Csv -LiteralPath $partitionCsvPath)) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $row "draw_index")
    if ($null -ne $draw -and -not $partitionByDraw.ContainsKey($draw)) {
        $partitionByDraw[$draw] = $row
    }
}

$drawRows = @(
    for ($draw = $drawStart; $draw -le $drawEnd; $draw++) {
        $coverage = if ($coverageByDraw.ContainsKey($draw)) { $coverageByDraw[$draw] } else { $null }
        $vertexSummary = if ($vertexSummaryByDraw.ContainsKey($draw)) { $vertexSummaryByDraw[$draw] } else { $null }
        $partition = if ($partitionByDraw.ContainsKey($draw)) { $partitionByDraw[$draw] } else { $null }
        $reason = Get-DrawReason -Coverage $coverage -VertexSummary $vertexSummary -Width $FrameWidth -Height $FrameHeight
        $primaryReason = Get-PrimaryReason $reason
        [pscustomobject][ordered]@{
            packet = $focusPacket
            draw_index = $draw
            draw_delta = $draw - $drawStart
            partition_kind = Get-ObjectValue $partition "partition_kind"
            texture_address = Get-ObjectValue $partition "texture_address"
            primary_reason = $primaryReason
            reason = $reason
            fifo_offset = Get-ObjectValue $coverage "fifo_offset"
            primitive = Get-ObjectValue $coverage "primitive"
            vertices = Get-ObjectValue $coverage "vertices"
            projected_vertices = Get-ObjectValue $coverage "projected_vertices"
            clipped_vertices = Get-ObjectValue $coverage "clipped_vertices"
            clip_input_triangles = Get-ObjectValue $coverage "clip_input_triangles"
            near_clip_output_triangles = Get-ObjectValue $coverage "near_clip_output_triangles"
            near_clip_culled_triangles = Get-ObjectValue $coverage "near_clip_culled_triangles"
            covered_pixels = Get-ObjectValue $coverage "covered_pixels"
            depth_rejected = Get-ObjectValue $coverage "depth_rejected"
            alpha_rejected = Get-ObjectValue $coverage "alpha_rejected"
            color_writes = Get-ObjectValue $coverage "color_writes"
            black_color_writes = Get-ObjectValue $coverage "black_color_writes"
            degenerate_triangles = Get-ObjectValue $coverage "degenerate_triangles_delta"
            screen_x_range = "$(Get-ObjectValue $vertexSummary "screen_min_x")..$(Get-ObjectValue $vertexSummary "screen_max_x")"
            screen_y_range = "$(Get-ObjectValue $vertexSummary "screen_min_y")..$(Get-ObjectValue $vertexSummary "screen_max_y")"
            view_z_range = "$(Get-ObjectValue $vertexSummary "view_min_z")..$(Get-ObjectValue $vertexSummary "view_max_z")"
            source_record_count = Get-ObjectValue $partition "source_record_count"
            source_record_range = Get-ObjectValue $partition "source_record_range"
            source_records = Get-ObjectValue $partition "source_records"
            output_indices = Get-ObjectValue $partition "output_indices"
            tex_s_range = Get-ObjectValue $partition "tex_s_range"
            tex_t_range = Get-ObjectValue $partition "tex_t_range"
        }
    }
)

$sequenceRows = New-Object System.Collections.Generic.List[object]
$current = $null
foreach ($row in $drawRows) {
    $signature = "{0}|{1}|{2}" -f (Get-ObjectValue $row "partition_kind"), (Get-ObjectValue $row "texture_address"), (Get-ObjectValue $row "primary_reason")
    if ($null -eq $current -or $current.Signature -ne $signature) {
        $current = [pscustomobject]@{
            Signature = $signature
            Rows = New-Object System.Collections.Generic.List[object]
        }
        $sequenceRows.Add($current)
    }

    $current.Rows.Add($row)
}

$sequenceOutputRows = @(
    $index = 0
    foreach ($sequence in $sequenceRows) {
        $index++
        $rows = @($sequence.Rows.ToArray())
        $draws = @($rows | ForEach-Object { [int](Get-ObjectValue $_ "draw_index") } | Sort-Object -Unique)
        $colorWrites = @($rows | ForEach-Object { Convert-ToNullableInt (Get-ObjectValue $_ "color_writes") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
        $blackWrites = @($rows | ForEach-Object { Convert-ToNullableInt (Get-ObjectValue $_ "black_color_writes") } | Where-Object { $null -ne $_ } | Measure-Object -Sum).Sum
        [pscustomobject][ordered]@{
            sequence_index = $index
            partition_kind = Get-ObjectValue $rows[0] "partition_kind"
            texture_address = Get-ObjectValue $rows[0] "texture_address"
            primary_reason = Get-ObjectValue $rows[0] "primary_reason"
            draw_start = $draws[0]
            draw_end = $draws[-1]
            draw_count = $draws.Count
            draws = $draws -join " "
            total_color_writes = if ($colorWrites -gt 0) { $colorWrites } else { 0 }
            total_black_color_writes = if ($blackWrites -gt 0) { $blackWrites } else { 0 }
            reason_tags = Format-Unique ($rows | ForEach-Object { [string](Get-ObjectValue $_ "reason") -split "\|" })
            source_records = Format-Unique ($rows | ForEach-Object { [string](Get-ObjectValue $_ "source_records") -split " " })
            output_indices = Format-Unique ($rows | ForEach-Object { [string](Get-ObjectValue $_ "output_indices") -split " " })
            screen_x_range = Format-Range ($rows | ForEach-Object { [string](Get-ObjectValue $_ "screen_x_range") -split "\.\." | ForEach-Object { Convert-ToNullableDouble $_ } })
            screen_y_range = Format-Range ($rows | ForEach-Object { [string](Get-ObjectValue $_ "screen_y_range") -split "\.\." | ForEach-Object { Convert-ToNullableDouble $_ } })
            view_z_range = Format-Range ($rows | ForEach-Object { [string](Get-ObjectValue $_ "view_z_range") -split "\.\." | ForEach-Object { Convert-ToNullableDouble $_ } })
            tex_s_range = Format-Range ($rows | ForEach-Object { [string](Get-ObjectValue $_ "tex_s_range") -split "\.\." | ForEach-Object { Convert-ToNullableDouble $_ } })
            tex_t_range = Format-Range ($rows | ForEach-Object { [string](Get-ObjectValue $_ "tex_t_range") -split "\.\." | ForEach-Object { Convert-ToNullableDouble $_ } })
        }
    }
)

$drawCsvPath = Join-Path $OutputDirectory "nonrendered-strip-draws.csv"
$sequenceCsvPath = Join-Path $OutputDirectory "nonrendered-strip-sequences.csv"
$jsonPath = Join-Path $OutputDirectory "nonrendered-strip-report.json"

$drawRows | Export-Csv -LiteralPath $drawCsvPath -NoTypeInformation -Encoding UTF8
$sequenceOutputRows | Export-Csv -LiteralPath $sequenceCsvPath -NoTypeInformation -Encoding UTF8

$report = [pscustomobject]([ordered]@{
    run_directory = $runRoot
    focus_packet = $focusPacket
    frame_width = $FrameWidth
    frame_height = $FrameHeight
    packet = $packetRow[0]
    draw_start = $drawStart
    draw_end = $drawEnd
    draws = [object[]]$drawRows
    sequences = [object[]]$sequenceOutputRows
})

$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic non-rendered strip report: $jsonPath"
Write-Output $sequenceOutputRows
