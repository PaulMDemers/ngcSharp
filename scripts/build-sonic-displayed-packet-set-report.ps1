param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,

    [string]$OutputDirectory = "",

    [string]$FocusPacket = "0x813184D0",

    [int]$DisplayCopyIndex = -1
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

function Import-OptionalCsv {
    param([string]$Path)

    if (Test-CsvHasRows $Path) {
        return @(Import-Csv -LiteralPath $Path)
    }

    return @()
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

function Convert-ToNullableDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Double {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Bounds {
    param([object]$Rect)

    if ($null -eq $Rect) {
        return ""
    }

    return "{0}/{1}-{2}/{3}" -f `
        (Format-Double $Rect.minX),
        (Format-Double $Rect.minY),
        (Format-Double $Rect.maxX),
        (Format-Double $Rect.maxY)
}

function Get-ClampedRect {
    param(
        [object]$DrawInfo,
        [double]$FrameWidth = 640.0,
        [double]$FrameHeight = 480.0
    )

    if ($null -eq $DrawInfo -or $null -eq $DrawInfo.minX -or $null -eq $DrawInfo.maxX -or $null -eq $DrawInfo.minY -or $null -eq $DrawInfo.maxY) {
        return $null
    }

    $minX = [Math]::Max(0.0, [Math]::Min($FrameWidth - 1.0, [double]$DrawInfo.minX))
    $maxX = [Math]::Max(0.0, [Math]::Min($FrameWidth - 1.0, [double]$DrawInfo.maxX))
    $minY = [Math]::Max(0.0, [Math]::Min($FrameHeight - 1.0, [double]$DrawInfo.minY))
    $maxY = [Math]::Max(0.0, [Math]::Min($FrameHeight - 1.0, [double]$DrawInfo.maxY))
    if ($maxX -lt $minX -or $maxY -lt $minY) {
        return $null
    }

    return [pscustomobject][ordered]@{
        minX = $minX
        minY = $minY
        maxX = $maxX
        maxY = $maxY
    }
}

function Get-OverlapArea {
    param(
        [object]$Rect,
        [object]$Region
    )

    if ($null -eq $Rect -or $null -eq $Region) {
        return 0.0
    }

    $left = [Math]::Max([double]$Rect.minX, [double]$Region.x)
    $top = [Math]::Max([double]$Rect.minY, [double]$Region.y)
    $right = [Math]::Min([double]$Rect.maxX, [double]$Region.x + [double]$Region.width)
    $bottom = [Math]::Min([double]$Rect.maxY, [double]$Region.y + [double]$Region.height)
    if ($right -le $left -or $bottom -le $top) {
        return 0.0
    }

    return ($right - $left) * ($bottom - $top)
}

function Join-Unique {
    param($Values)

    return (@($Values | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique) -join " ")
}

function New-Aggregate {
    return [pscustomobject][ordered]@{
        draw_index = ""
        attribution_scope = ""
        packet = ""
        packet_kind = ""
        object = ""
        object_kind = ""
        texture_address = ""
        texture_format = ""
        texture_size = ""
        stage0_mode = ""
        draw_count = 0L
        triangle_rows = 0L
        covered_pixels = 0L
        color_writes = 0L
        black_color_writes = 0L
        first_draw = $null
        last_draw = $null
        screen_bounds = ""
        clamped_bounds = ""
        region_tags = ""
        skyline_overlap = 0.0
        bridge_overlap = 0.0
        lower_track_overlap = 0.0
        decoded_vertices = 0L
        clipped_vertices = 0L
        fifo_offset = ""
    }
}

function Add-AggregateRow {
    param(
        [hashtable]$Groups,
        [string]$Key,
        [object]$Row,
        [string[]]$RegionNames
    )

    if (-not $Groups.ContainsKey($Key)) {
        $Groups[$Key] = [pscustomobject][ordered]@{
            rows = @()
            drawSet = @{}
            regionSet = @{}
            aggregate = New-Aggregate
        }
    }

    $state = $Groups[$Key]
    $state.rows += $Row
    if (-not [string]::IsNullOrWhiteSpace([string]$Row.draw_index)) {
        $state.drawSet[[string]$Row.draw_index] = $true
    }

    foreach ($regionName in $RegionNames) {
        $state.regionSet[$regionName] = $true
    }

    $aggregate = $state.aggregate
    $aggregate.triangle_rows += [int64]$Row.triangle_rows
    $aggregate.covered_pixels += [int64]$Row.covered_pixels
    $aggregate.color_writes += [int64]$Row.color_writes
    $aggregate.black_color_writes += [int64]$Row.black_color_writes
    $aggregate.skyline_overlap += [double]$Row.skyline_overlap
    $aggregate.bridge_overlap += [double]$Row.bridge_overlap
    $aggregate.lower_track_overlap += [double]$Row.lower_track_overlap
    $aggregate.decoded_vertices += [int64]$Row.decoded_vertices
    $aggregate.clipped_vertices += [int64]$Row.clipped_vertices
    $draw = Convert-ToNullableInt64 $Row.draw_index
    if ($null -ne $draw) {
        if ($null -eq $aggregate.first_draw -or $draw -lt $aggregate.first_draw) {
            $aggregate.first_draw = $draw
        }

        if ($null -eq $aggregate.last_draw -or $draw -gt $aggregate.last_draw) {
            $aggregate.last_draw = $draw
        }
    }
}

function Complete-Aggregates {
    param(
        [hashtable]$Groups,
        [scriptblock]$Hydrate
    )

    $results = foreach ($entry in $Groups.GetEnumerator()) {
        $state = $entry.Value
        $aggregate = $state.aggregate
        $sample = $state.rows[0]
        & $Hydrate $aggregate $sample
        $aggregate.draw_count = [int64]$state.drawSet.Count
        $aggregate.region_tags = Join-Unique $state.regionSet.Keys
        $aggregate.skyline_overlap = [Math]::Round([double]$aggregate.skyline_overlap, 3)
        $aggregate.bridge_overlap = [Math]::Round([double]$aggregate.bridge_overlap, 3)
        $aggregate.lower_track_overlap = [Math]::Round([double]$aggregate.lower_track_overlap, 3)
        if ($aggregate.color_writes -gt 0) {
            $aggregate | Add-Member -NotePropertyName black_write_ratio -NotePropertyValue ([Math]::Round([double]$aggregate.black_color_writes / [double]$aggregate.color_writes, 6))
        } else {
            $aggregate | Add-Member -NotePropertyName black_write_ratio -NotePropertyValue 0.0
        }

        $aggregate
    }

    return @($results | Sort-Object @{ Expression = "color_writes"; Descending = $true }, @{ Expression = "covered_pixels"; Descending = $true })
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-displayed-packet-set"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$triangleSummaryPath = Join-Path $runRoot "gx-triangle-coverage.summary.csv"
$verticesSummaryPath = Join-Path $runRoot "gx-vertices.summary.csv"
$packetTimelinePath = Join-Path $runRoot "sonic-packet-timeline.csv"
$copiesPath = Join-Path $runRoot "gx-copies.csv"

if (-not (Test-CsvHasRows $triangleSummaryPath)) {
    throw "GX triangle coverage summary not found or empty: $triangleSummaryPath"
}

if (-not (Test-CsvHasRows $verticesSummaryPath)) {
    throw "GX vertices summary not found or empty: $verticesSummaryPath"
}

$focusPacketNormalized = Normalize-Hex $FocusPacket
$regions = @(
    [pscustomobject][ordered]@{ name = "skyline"; x = 48.0; y = 72.0; width = 544.0; height = 170.0 },
    [pscustomobject][ordered]@{ name = "bridge"; x = 116.0; y = 156.0; width = 420.0; height = 210.0 },
    [pscustomobject][ordered]@{ name = "lower-track"; x = 92.0; y = 264.0; width = 456.0; height = 178.0 }
)

$drawInfoByDraw = @{}
foreach ($row in (Import-Csv -LiteralPath $verticesSummaryPath)) {
    $draw = Convert-ToNullableInt64 (Get-ObjectValue $row "draw_index")
    if ($null -eq $draw) {
        continue
    }

    $minX = Convert-ToNullableDouble (Get-ObjectValue $row "screen_min_x")
    $maxX = Convert-ToNullableDouble (Get-ObjectValue $row "screen_max_x")
    $minY = Convert-ToNullableDouble (Get-ObjectValue $row "screen_min_y")
    $maxY = Convert-ToNullableDouble (Get-ObjectValue $row "screen_max_y")
    $decoded = Convert-ToNullableInt64 (Get-ObjectValue $row "decoded_vertices")
    $clipped = Convert-ToNullableInt64 (Get-ObjectValue $row "clipped_vertices")
    $key = [string]$draw
    if (-not $drawInfoByDraw.ContainsKey($key)) {
        $drawInfoByDraw[$key] = [pscustomobject][ordered]@{
            draw = $draw
            minX = $minX
            maxX = $maxX
            minY = $minY
            maxY = $maxY
            decoded = if ($null -eq $decoded) { 0L } else { $decoded }
            clipped = if ($null -eq $clipped) { 0L } else { $clipped }
            fifo = [string](Get-ObjectValue $row "fifo_offset")
        }
        continue
    }

    $existing = $drawInfoByDraw[$key]
    if ($null -ne $minX) { $existing.minX = if ($null -eq $existing.minX) { $minX } else { [Math]::Min([double]$existing.minX, [double]$minX) } }
    if ($null -ne $maxX) { $existing.maxX = if ($null -eq $existing.maxX) { $maxX } else { [Math]::Max([double]$existing.maxX, [double]$maxX) } }
    if ($null -ne $minY) { $existing.minY = if ($null -eq $existing.minY) { $minY } else { [Math]::Min([double]$existing.minY, [double]$minY) } }
    if ($null -ne $maxY) { $existing.maxY = if ($null -eq $existing.maxY) { $maxY } else { [Math]::Max([double]$existing.maxY, [double]$maxY) } }
    $existing.decoded += if ($null -eq $decoded) { 0L } else { $decoded }
    $existing.clipped += if ($null -eq $clipped) { 0L } else { $clipped }
}

$packetByDraw = @{}
$mappedPacketRows = @()
foreach ($row in (Import-OptionalCsv $packetTimelinePath)) {
    $packet = Normalize-Hex ([string](Get-ObjectValue $row "packet"))
    $start = Convert-ToNullableInt64 (Get-ObjectValue $row "mapped_draw_start")
    $end = Convert-ToNullableInt64 (Get-ObjectValue $row "mapped_draw_end")
    if ([string]::IsNullOrWhiteSpace($packet) -or $null -eq $start -or $null -eq $end) {
        continue
    }

    $mappedPacketRows += $row
    for ($draw = $start; $draw -le $end; $draw++) {
        $key = [string]$draw
        if (-not $packetByDraw.ContainsKey($key)) {
            $packetByDraw[$key] = $row
        }
    }
}

$displayCopy = $null
$copyRows = @(Import-OptionalCsv $copiesPath | Where-Object { [string](Get-ObjectValue $_ "kind") -eq "display" })
if ($DisplayCopyIndex -ge 0) {
    $displayCopy = $copyRows | Where-Object { (Convert-ToNullableInt64 (Get-ObjectValue $_ "copy_index")) -eq $DisplayCopyIndex } | Select-Object -First 1
}

if ($null -eq $displayCopy -and $copyRows.Count -gt 0) {
    $displayCopy = $copyRows |
        Sort-Object @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "display_nonblack") }; Descending = $true }, @{ Expression = { Convert-ToNullableInt64 (Get-ObjectValue $_ "draws_seen") }; Descending = $true } |
        Select-Object -First 1
}

$drawMaterialGroups = @{}
$textureGroups = @{}
$packetGroups = @{}
$regionGroups = @{}
$totalColorWrites = 0L
$totalBlackWrites = 0L
$totalCoveredPixels = 0L

foreach ($coverage in (Import-Csv -LiteralPath $triangleSummaryPath)) {
    $draw = Convert-ToNullableInt64 (Get-ObjectValue $coverage "draw_index")
    if ($null -eq $draw) {
        continue
    }

    $colorWrites = Convert-ToNullableInt64 (Get-ObjectValue $coverage "color_writes")
    $blackWrites = Convert-ToNullableInt64 (Get-ObjectValue $coverage "black_color_writes")
    $coveredPixels = Convert-ToNullableInt64 (Get-ObjectValue $coverage "covered_pixels")
    if ($null -eq $colorWrites) { $colorWrites = 0L }
    if ($null -eq $blackWrites) { $blackWrites = 0L }
    if ($null -eq $coveredPixels) { $coveredPixels = 0L }

    $drawInfo = $drawInfoByDraw[[string]$draw]
    $rawBounds = $null
    if ($null -ne $drawInfo) {
        $rawBounds = [pscustomobject][ordered]@{
            minX = $drawInfo.minX
            minY = $drawInfo.minY
            maxX = $drawInfo.maxX
            maxY = $drawInfo.maxY
        }
    }

    $clampedBounds = Get-ClampedRect $drawInfo
    $regionNames = @()
    $overlapByRegion = @{}
    foreach ($region in $regions) {
        $overlap = Get-OverlapArea $clampedBounds $region
        $overlapByRegion[$region.name] = $overlap
        if ($overlap -gt 0.0) {
            $regionNames += $region.name
        }
    }

    if ($regionNames.Count -eq 0) {
        $regionNames += "outside-anchor-regions"
    }

    $packetRow = $packetByDraw[[string]$draw]
    $packet = ""
    $packetKind = ""
    $object = ""
    $objectKind = ""
    $scope = "unmapped-draw"
    if ($null -ne $packetRow) {
        $packet = Normalize-Hex ([string](Get-ObjectValue $packetRow "packet"))
        $packetKind = [string](Get-ObjectValue $packetRow "packet_kind")
        $object = Normalize-Hex ([string](Get-ObjectValue $packetRow "object"))
        $objectKind = [string](Get-ObjectValue $packetRow "object_kind")
        $scope = "exact-packet-draw-map"
    }

    $texture = Normalize-Hex ([string](Get-ObjectValue $coverage "texture_address"))
    if ([string]::IsNullOrWhiteSpace($texture)) {
        $texture = "none"
    }

    $rowOut = [pscustomobject][ordered]@{
        draw_index = $draw
        attribution_scope = $scope
        packet = $packet
        packet_kind = $packetKind
        object = $object
        object_kind = $objectKind
        texture_address = $texture
        texture_format = [string](Get-ObjectValue $coverage "texture_format")
        texture_size = [string](Get-ObjectValue $coverage "texture_size")
        stage0_mode = [string](Get-ObjectValue $coverage "stage0_mode")
        triangle_rows = 1L
        covered_pixels = $coveredPixels
        color_writes = $colorWrites
        black_color_writes = $blackWrites
        black_write_ratio = if ($colorWrites -gt 0) { [Math]::Round([double]$blackWrites / [double]$colorWrites, 6) } else { 0.0 }
        screen_bounds = Format-Bounds $rawBounds
        clamped_bounds = Format-Bounds $clampedBounds
        region_tags = Join-Unique $regionNames
        skyline_overlap = [Math]::Round([double]$overlapByRegion["skyline"], 3)
        bridge_overlap = [Math]::Round([double]$overlapByRegion["bridge"], 3)
        lower_track_overlap = [Math]::Round([double]$overlapByRegion["lower-track"], 3)
        decoded_vertices = if ($null -eq $drawInfo) { 0L } else { [int64]$drawInfo.decoded }
        clipped_vertices = if ($null -eq $drawInfo) { 0L } else { [int64]$drawInfo.clipped }
        fifo_offset = if ($null -eq $drawInfo) { "" } else { [string]$drawInfo.fifo }
    }

    $totalColorWrites += $colorWrites
    $totalBlackWrites += $blackWrites
    $totalCoveredPixels += $coveredPixels

    $drawKey = "{0}|{1}|{2}|{3}|{4}" -f $draw, $scope, $packet, $texture, $rowOut.stage0_mode
    Add-AggregateRow $drawMaterialGroups $drawKey $rowOut $regionNames

    $textureKey = "{0}|{1}|{2}|{3}" -f $texture, $rowOut.texture_format, $rowOut.texture_size, $rowOut.stage0_mode
    Add-AggregateRow $textureGroups $textureKey $rowOut $regionNames

    $packetKey = "{0}|{1}|{2}|{3}|{4}" -f $scope, $packet, $packetKind, $object, $objectKind
    Add-AggregateRow $packetGroups $packetKey $rowOut $regionNames

    foreach ($regionName in $regionNames) {
        $regionKey = "{0}|{1}|{2}|{3}|{4}" -f $regionName, $texture, $rowOut.texture_format, $rowOut.texture_size, $rowOut.stage0_mode
        Add-AggregateRow $regionGroups $regionKey $rowOut @($regionName)
    }
}

$drawMaterialRows = @(Complete-Aggregates $drawMaterialGroups {
    param($aggregate, $sample)
    $aggregate.draw_index = $sample.draw_index
    $aggregate.attribution_scope = $sample.attribution_scope
    $aggregate.packet = $sample.packet
    $aggregate.packet_kind = $sample.packet_kind
    $aggregate.object = $sample.object
    $aggregate.object_kind = $sample.object_kind
    $aggregate.texture_address = $sample.texture_address
    $aggregate.texture_format = $sample.texture_format
    $aggregate.texture_size = $sample.texture_size
    $aggregate.stage0_mode = $sample.stage0_mode
    $aggregate.screen_bounds = $sample.screen_bounds
    $aggregate.clamped_bounds = $sample.clamped_bounds
    $aggregate.fifo_offset = $sample.fifo_offset
})

$textureRows = @(Complete-Aggregates $textureGroups {
    param($aggregate, $sample)
    $aggregate.attribution_scope = "all-displayed-draws"
    $aggregate.texture_address = $sample.texture_address
    $aggregate.texture_format = $sample.texture_format
    $aggregate.texture_size = $sample.texture_size
    $aggregate.stage0_mode = $sample.stage0_mode
})

$packetRows = @(Complete-Aggregates $packetGroups {
    param($aggregate, $sample)
    $aggregate.attribution_scope = $sample.attribution_scope
    $aggregate.packet = $sample.packet
    $aggregate.packet_kind = $sample.packet_kind
    $aggregate.object = $sample.object
    $aggregate.object_kind = $sample.object_kind
})

$regionRows = @(Complete-Aggregates $regionGroups {
    param($aggregate, $sample)
    $aggregate.attribution_scope = "region-material-ranking"
    $aggregate.texture_address = $sample.texture_address
    $aggregate.texture_format = $sample.texture_format
    $aggregate.texture_size = $sample.texture_size
    $aggregate.stage0_mode = $sample.stage0_mode
})

$drawMaterialPath = Join-Path $outputRoot "displayed-draw-materials.csv"
$packetSetPath = Join-Path $outputRoot "displayed-packet-set.csv"
$textureRankingPath = Join-Path $outputRoot "displayed-texture-ranking.csv"
$regionRankingPath = Join-Path $outputRoot "displayed-region-ranking.csv"
$jsonPath = Join-Path $outputRoot "displayed-packet-set-report.json"

$drawMaterialRows | Export-Csv -NoTypeInformation -LiteralPath $drawMaterialPath
$packetRows | Export-Csv -NoTypeInformation -LiteralPath $packetSetPath
$textureRows | Export-Csv -NoTypeInformation -LiteralPath $textureRankingPath
$regionRows | Export-Csv -NoTypeInformation -LiteralPath $regionRankingPath

$focusRows = @($drawMaterialRows | Where-Object { [string]$_.packet -eq $focusPacketNormalized })
$focusColorWrites = 0L
$focusBlackWrites = 0L
foreach ($row in $focusRows) {
    $focusColorWrites += [int64]$row.color_writes
    $focusBlackWrites += [int64]$row.black_color_writes
}

$exactMappedColorWrites = 0L
foreach ($row in ($drawMaterialRows | Where-Object { [string]$_.attribution_scope -eq "exact-packet-draw-map" })) {
    $exactMappedColorWrites += [int64]$row.color_writes
}

$summary = [pscustomobject][ordered]@{
    runDirectory = $runRoot
    outputDirectory = $outputRoot
    focusPacket = $focusPacketNormalized
    displayCopy = if ($null -eq $displayCopy) {
        $null
    } else {
        [ordered]@{
            copyIndex = Get-ObjectValue $displayCopy "copy_index"
            drawsSeen = Get-ObjectValue $displayCopy "draws_seen"
            displayNonblack = Get-ObjectValue $displayCopy "display_nonblack"
            displayNonblackPercent = Get-ObjectValue $displayCopy "display_nonblack_percent"
            displayNonblackBounds = Get-ObjectValue $displayCopy "display_nonblack_bounds"
        }
    }
    rows = [ordered]@{
        drawMaterials = $drawMaterialRows.Count
        packetBuckets = $packetRows.Count
        textureBuckets = $textureRows.Count
        regionBuckets = $regionRows.Count
        mappedPacketTimelineRows = $mappedPacketRows.Count
    }
    totals = [ordered]@{
        coveredPixels = $totalCoveredPixels
        colorWrites = $totalColorWrites
        blackColorWrites = $totalBlackWrites
        blackWriteRatio = if ($totalColorWrites -gt 0) { [Math]::Round([double]$totalBlackWrites / [double]$totalColorWrites, 6) } else { 0.0 }
        exactMappedColorWrites = $exactMappedColorWrites
    }
    focusPacketTotals = [ordered]@{
        drawMaterialRows = $focusRows.Count
        colorWrites = $focusColorWrites
        blackColorWrites = $focusBlackWrites
        blackWriteRatio = if ($focusColorWrites -gt 0) { [Math]::Round([double]$focusBlackWrites / [double]$focusColorWrites, 6) } else { 0.0 }
        firstDraw = if ($focusRows.Count -eq 0) { $null } else { ($focusRows | Measure-Object first_draw -Minimum).Minimum }
        lastDraw = if ($focusRows.Count -eq 0) { $null } else { ($focusRows | Measure-Object last_draw -Maximum).Maximum }
    }
    attributionNote = "Only draws present in sonic-packet-timeline.csv mapped_draw_start/end ranges have exact packet attribution. Other displayed draw/material rows are ranked globally and marked unmapped-draw."
    topTextures = @($textureRows | Select-Object -First 12)
    outputFiles = [ordered]@{
        displayedDrawMaterialsCsv = $drawMaterialPath
        displayedPacketSetCsv = $packetSetPath
        displayedTextureRankingCsv = $textureRankingPath
        displayedRegionRankingCsv = $regionRankingPath
        reportJson = $jsonPath
    }
}

$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Wrote displayed packet/material report to $outputRoot"
