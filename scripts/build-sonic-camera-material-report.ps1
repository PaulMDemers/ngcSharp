param(
    [Parameter(Mandatory = $true)]
    [string]$RunDirectory,
    [string]$OutputDirectory = "",
    [string[]]$FocusTextureAddresses = @("0x0072C600", "0x0071BC60")
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

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
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
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "0x{0:X8}" -f ([uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
    }

    return "0x{0:X8}" -f ([uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture))
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

function Get-FirstByDraw {
    param(
        [hashtable]$Map,
        [object]$Draw
    )

    $drawIndex = Convert-ToNullableInt $Draw
    if ($null -eq $drawIndex -or -not $Map.ContainsKey($drawIndex)) {
        return $null
    }

    return $Map[$drawIndex]
}

function Get-DrawsFromMaterial {
    param($Material)

    $drawsText = [string](Get-ObjectValue $Material "draws")
    if ([string]::IsNullOrWhiteSpace($drawsText)) {
        return @()
    }

    return @(
        $drawsText -split '\s+' |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { Convert-ToNullableInt $_ } |
            Where-Object { $null -ne $_ } |
            Sort-Object -Unique
    )
}

function Get-TriangleCountsByDraw {
    param([object[]]$Rows)

    $map = @{}
    foreach ($group in ($Rows | Group-Object draw_index)) {
        $rendered = @($group.Group | Where-Object { [string](Get-ObjectValue $_ "color_writes") -ne "0" })
        $coveredPixels = ($group.Group | ForEach-Object { [int64](Get-ObjectValue $_ "covered_pixels" 0) } | Measure-Object -Sum).Sum
        $colorWrites = ($group.Group | ForEach-Object { [int64](Get-ObjectValue $_ "color_writes" 0) } | Measure-Object -Sum).Sum
        $blackWrites = ($group.Group | ForEach-Object { [int64](Get-ObjectValue $_ "black_color_writes" 0) } | Measure-Object -Sum).Sum
        $map[[int]$group.Name] = [pscustomobject]@{
            rendered_triangles = $rendered.Count
            covered_pixels = [int64]$coveredPixels
            color_writes = [int64]$colorWrites
            black_color_writes = [int64]$blackWrites
        }
    }

    return $map
}

function Get-PacketForDraw {
    param(
        [object[]]$Packets,
        [object]$Draw
    )

    $drawIndex = Convert-ToNullableInt $Draw
    if ($null -eq $drawIndex) {
        return $null
    }

    return $Packets |
        Where-Object {
            $start = Convert-ToNullableInt (Get-ObjectValue $_ "mapped_draw_start")
            $end = Convert-ToNullableInt (Get-ObjectValue $_ "mapped_draw_end")
            $null -ne $start -and $null -ne $end -and $drawIndex -ge $start -and $drawIndex -le $end
        } |
        Select-Object -First 1
}

function New-CameraRow {
    param(
        [object]$Transform,
        [object]$Coverage,
        [object]$Triangle,
        [string]$TextureAddress = "",
        [object]$Material = $null,
        [object]$Packet = $null
    )

    [pscustomobject][ordered]@{
        draw_index = Get-ObjectValue $Transform "draw_index"
        fifo_offset = Get-ObjectValue $Transform "fifo_offset"
        packet = Get-ObjectValue $Packet "packet"
        packet_kind = Get-ObjectValue $Packet "packet_kind"
        object = Get-ObjectValue $Packet "object"
        object_kind = Get-ObjectValue $Packet "object_kind"
        object_xyz = Get-ObjectValue $Packet "object_xyz"
        matrix_translation = Get-ObjectValue $Packet "matrix_translation"
        packet_mapped_draw_start = Get-ObjectValue $Packet "mapped_draw_start"
        packet_mapped_draw_end = Get-ObjectValue $Packet "mapped_draw_end"
        packet_mapped_fifo_start = Get-ObjectValue $Packet "mapped_fifo_start"
        packet_mapped_fifo_end = Get-ObjectValue $Packet "mapped_fifo_end"
        packet_decoded_vertices = Get-ObjectValue $Packet "decoded_vertices"
        packet_clipped_vertices = Get-ObjectValue $Packet "clipped_vertices"
        packet_view_bounds = Get-ObjectValue $Packet "view_bounds"
        packet_screen_bounds = Get-ObjectValue $Packet "screen_bounds"
        texture_address = $TextureAddress
        material_draws = Get-ObjectValue $Material "draws"
        material_triangles = Get-ObjectValue $Material "triangles"
        material_black_ratio = Get-ObjectValue $Material "black_write_ratio"
        material_covered_pixels = Get-ObjectValue $Material "covered_pixels"
        material_color_writes = Get-ObjectValue $Material "color_writes"
        material_black_writes = Get-ObjectValue $Material "black_color_writes"
        primitive = Get-ObjectValue $Transform "primitive"
        vertices = Get-ObjectValue $Transform "vertices"
        decoded = Get-ObjectValue $Transform "decoded"
        projected_vertices = Get-ObjectValue $Transform "projected_vertices"
        clipped_vertices = Get-ObjectValue $Transform "clipped_vertices"
        matrix_index_raw = Get-ObjectValue $Transform "matrix_index_raw"
        pos_base = Get-ObjectValue $Transform "pos_base"
        projection_type = Get-ObjectValue $Transform "projection_type"
        projection_00 = Get-ObjectValue $Transform "projection_00"
        projection_01_or_03 = Get-ObjectValue $Transform "projection_01_or_03"
        projection_11 = Get-ObjectValue $Transform "projection_11"
        projection_12_or_13 = Get-ObjectValue $Transform "projection_12_or_13"
        projection_22 = Get-ObjectValue $Transform "projection_22"
        projection_23 = Get-ObjectValue $Transform "projection_23"
        viewport_scale_x = Get-ObjectValue $Transform "viewport_scale_x"
        viewport_scale_y = Get-ObjectValue $Transform "viewport_scale_y"
        viewport_scale_z = Get-ObjectValue $Transform "viewport_scale_z"
        viewport_origin_x = Get-ObjectValue $Transform "viewport_origin_x"
        viewport_origin_y = Get-ObjectValue $Transform "viewport_origin_y"
        viewport_origin_z = Get-ObjectValue $Transform "viewport_origin_z"
        model_bounds = "$(Get-ObjectValue $Transform "model_min_x")/$(Get-ObjectValue $Transform "model_min_y")/$(Get-ObjectValue $Transform "model_min_z")-$(Get-ObjectValue $Transform "model_max_x")/$(Get-ObjectValue $Transform "model_max_y")/$(Get-ObjectValue $Transform "model_max_z")"
        view_bounds = "$(Get-ObjectValue $Transform "view_min_x")/$(Get-ObjectValue $Transform "view_min_y")/$(Get-ObjectValue $Transform "view_min_z")-$(Get-ObjectValue $Transform "view_max_x")/$(Get-ObjectValue $Transform "view_max_y")/$(Get-ObjectValue $Transform "view_max_z")"
        screen_bounds = "$(Get-ObjectValue $Transform "screen_min_x")/$(Get-ObjectValue $Transform "screen_min_y")/$(Get-ObjectValue $Transform "screen_min_z")-$(Get-ObjectValue $Transform "screen_max_x")/$(Get-ObjectValue $Transform "screen_max_y")/$(Get-ObjectValue $Transform "screen_max_z")"
        first_model = Get-ObjectValue $Transform "first_model"
        first_view = Get-ObjectValue $Transform "first_view"
        first_screen = Get-ObjectValue $Transform "first_screen"
        draw_after_nonblack = Get-ObjectValue $Coverage "after_nonblack"
        draw_delta_nonblack = Get-ObjectValue $Coverage "delta_nonblack"
        draw_covered_pixels = Get-ObjectValue $Coverage "covered_pixels"
        draw_color_writes = Get-ObjectValue $Coverage "color_writes"
        draw_black_writes = Get-ObjectValue $Coverage "black_color_writes"
        draw_clip_input_triangles = Get-ObjectValue $Coverage "clip_input_triangles"
        draw_near_clip_output_triangles = Get-ObjectValue $Coverage "near_clip_output_triangles"
        draw_near_clip_culled_triangles = Get-ObjectValue $Coverage "near_clip_culled_triangles"
        triangle_rendered_rows = Get-ObjectValue $Triangle "rendered_triangles"
        triangle_covered_pixels = Get-ObjectValue $Triangle "covered_pixels"
        triangle_color_writes = Get-ObjectValue $Triangle "color_writes"
        triangle_black_writes = Get-ObjectValue $Triangle "black_color_writes"
    }
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-camera-material-report"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$runJsonPath = Join-Path $runRoot "run.json"
$emulatorSummaryPath = Join-Path $runRoot "emulator-summary.json"
$transformCsvPath = Join-Path $runRoot "gx-transforms.csv"
$coverageCsvPath = Join-Path $runRoot "gx-coverage.csv"
$triangleSummaryCsvPath = Join-Path $runRoot "gx-triangle-coverage.summary.csv"
$materialCsvPath = Join-Path $runRoot "gx-materials.summary.csv"
$textureIndexPath = Join-Path $runRoot "textures\index.csv"
$packetTimelinePath = Join-Path $runRoot "sonic-packet-timeline.csv"

if (-not (Test-CsvHasRows $transformCsvPath)) {
    throw "Transform CSV missing or empty: $transformCsvPath"
}

$run = Read-JsonFile $runJsonPath
$emulatorSummary = Read-JsonFile $emulatorSummaryPath
$frameDump = Get-ObjectValue (Get-ObjectValue $emulatorSummary "gx" $null) "frameDump" $null
$lifecycle = Get-ObjectValue $frameDump "lifecycle" $null
$selectedCopy = Get-ObjectValue $lifecycle "selectedCopy" $null
if ($null -eq $selectedCopy) {
    $selectedCopy = Get-ObjectValue $lifecycle "lastDisplayCopy" $null
}

$transforms = @(Import-Csv -LiteralPath $transformCsvPath)
$coverageRows = if (Test-CsvHasRows $coverageCsvPath) { @(Import-Csv -LiteralPath $coverageCsvPath) } else { @() }
$triangleRows = if (Test-CsvHasRows $triangleSummaryCsvPath) { @(Import-Csv -LiteralPath $triangleSummaryCsvPath) } else { @() }
$materialRows = if (Test-CsvHasRows $materialCsvPath) { @(Import-Csv -LiteralPath $materialCsvPath) } else { @() }
$textureRows = if (Test-CsvHasRows $textureIndexPath) { @(Import-Csv -LiteralPath $textureIndexPath) } else { @() }
$packetRowsRaw = if (Test-CsvHasRows $packetTimelinePath) { @(Import-Csv -LiteralPath $packetTimelinePath) } else { @() }
$packetRows = @(
    $packetRowsRaw |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string](Get-ObjectValue $_ "mapped_draw_start")) } |
        Sort-Object packet, mapped_draw_start, mapped_draw_end -Unique
)

$coverageByDraw = @{}
foreach ($row in $coverageRows) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $row "draw_index")
    if ($null -ne $draw) {
        $coverageByDraw[$draw] = $row
    }
}

$triangleByDraw = Get-TriangleCountsByDraw $triangleRows
$transformByDraw = @{}
foreach ($row in $transforms) {
    $draw = Convert-ToNullableInt (Get-ObjectValue $row "draw_index")
    if ($null -ne $draw) {
        $transformByDraw[$draw] = $row
    }
}

$focusTextures = @($FocusTextureAddresses | ForEach-Object { Normalize-Hex $_ })
$focusMaterials = @(
    foreach ($texture in $focusTextures) {
        $materialRows |
            Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "texture_address"))) -eq $texture } |
            Select-Object -First 1
    }
)

$materialCameraRows = New-Object System.Collections.Generic.List[object]
foreach ($material in $focusMaterials) {
    $texture = Normalize-Hex ([string](Get-ObjectValue $material "texture_address"))
    foreach ($draw in Get-DrawsFromMaterial $material) {
        $transform = Get-FirstByDraw $transformByDraw $draw
        if ($null -eq $transform) {
            continue
        }

        $coverage = Get-FirstByDraw $coverageByDraw $draw
        $triangle = Get-FirstByDraw $triangleByDraw $draw
        $packet = Get-PacketForDraw $packetRows $draw
        $materialCameraRows.Add((New-CameraRow -Transform $transform -Coverage $coverage -Triangle $triangle -TextureAddress $texture -Material $material -Packet $packet))
    }
}

$drawCameraRows = @(
    foreach ($transform in $transforms) {
        $draw = Convert-ToNullableInt (Get-ObjectValue $transform "draw_index")
        $coverage = Get-FirstByDraw $coverageByDraw $draw
        $triangle = Get-FirstByDraw $triangleByDraw $draw
        $packet = Get-PacketForDraw $packetRows $draw
        New-CameraRow -Transform $transform -Coverage $coverage -Triangle $triangle -Packet $packet
    }
)

$projectionGroups = @(
    $transforms |
        Group-Object projection_type, projection_00, projection_11, projection_22, projection_23, viewport_scale_x, viewport_scale_y, viewport_origin_x, viewport_origin_y |
        Sort-Object Count -Descending |
        ForEach-Object {
            [pscustomobject][ordered]@{
                signature = $_.Name
                count = $_.Count
                first_draw = Get-ObjectValue ($_.Group | Select-Object -First 1) "draw_index"
                last_draw = Get-ObjectValue ($_.Group | Select-Object -Last 1) "draw_index"
            }
        }
)

$textureStats = @(
    foreach ($texture in $focusTextures) {
        $mip0 = $textureRows |
            Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "source_address"))) -eq $texture -and [string](Get-ObjectValue $_ "mip_level") -eq "0" } |
            Select-Object -First 1
        [pscustomobject][ordered]@{
            texture_address = $texture
            mip0_path = if ($null -ne $mip0) { Join-Path (Split-Path -Parent $textureIndexPath) ([string](Get-ObjectValue $mip0 "path")) } else { "" }
            mip0_hash = Get-ObjectValue $mip0 "source_hash"
            mip0_nonblack_pixels = Get-ObjectValue $mip0 "nonblack_pixels"
            mip0_nontransparent_pixels = Get-ObjectValue $mip0 "nontransparent_pixels"
            mip0_samples = Get-ObjectValue $mip0 "samples"
        }
    }
)

$selectedCopyDraw = Convert-ToNullableInt (Get-ObjectValue $selectedCopy "drawsSeen")
$windowStart = Convert-ToNullableInt (Get-ObjectValue $run "gxFrameSkipDraws")
$windowDraws = Convert-ToNullableInt (Get-ObjectValue $run "gxFrameMaxDraws")
$windowEnd = if ($null -ne $windowStart -and $null -ne $windowDraws) { $windowStart + $windowDraws } else { $null }
$focusMaterialSummary = @(
    foreach ($material in $focusMaterials) {
        [pscustomobject][ordered]@{
            texture_address = Get-ObjectValue $material "texture_address"
            texture_format = Get-ObjectValue $material "texture_format"
            texture_size = Get-ObjectValue $material "texture_size"
            stage0_mode = Get-ObjectValue $material "stage0_mode"
            draws = Get-ObjectValue $material "draws"
            triangle_count = Get-ObjectValue $material "triangle_count"
            covered_pixels = Get-ObjectValue $material "covered_pixels"
            color_writes = Get-ObjectValue $material "color_writes"
            black_color_writes = Get-ObjectValue $material "black_color_writes"
            black_write_ratio = Get-ObjectValue $material "black_write_ratio"
            uv_s_min = Get-ObjectValue $material "uv_s_min"
            uv_s_max = Get-ObjectValue $material "uv_s_max"
            uv_t_min = Get-ObjectValue $material "uv_t_min"
            uv_t_max = Get-ObjectValue $material "uv_t_max"
            view_w_min = Get-ObjectValue $material "view_w_min"
            view_w_max = Get-ObjectValue $material "view_w_max"
            sample_raster_rgba_top = Get-ObjectValue $material "sample_raster_rgba_top"
            sample_tev_rgba_top = Get-ObjectValue $material "sample_tev_rgba_top"
        }
    }
)

$overview = [pscustomobject][ordered]@{
    run_directory = $runRoot
    target = Get-ObjectValue $run "target"
    status = Get-ObjectValue $run "status"
    configuration = Get-ObjectValue $run "configuration"
    selected_copy_draw = $selectedCopyDraw
    selected_copy_index = Get-ObjectValue $selectedCopy "copyIndex"
    selected_copy_fifo_offset = Get-ObjectValue $selectedCopy "fifoOffset"
    selected_copy_destination = Get-ObjectValue $selectedCopy "destinationAddress"
    selected_copy_clear_after_copy = Get-ObjectValue $selectedCopy "clearAfterCopy"
    lifecycle_phase = Get-ObjectValue $lifecycle "phase"
    draws_since_selected_copy = Get-ObjectValue $lifecycle "drawsSinceLastDisplayCopy"
    material_window_start = $windowStart
    material_window_end = $windowEnd
    material_window_draws = $windowDraws
    transform_draws = $transforms.Count
    packet_timeline_rows = $packetRows.Count
    projection_signature_count = $projectionGroups.Count
    focus_textures = $focusTextures -join " "
    focus_material_count = $focusMaterials.Count
    focus_material_draws = (($focusMaterials | ForEach-Object { Get-ObjectValue $_ "draws" }) -join " | ")
}

$overviewCsvPath = Join-Path $OutputDirectory "camera-material-overview.csv"
$materialCameraCsvPath = Join-Path $OutputDirectory "camera-material-focus.csv"
$drawCameraCsvPath = Join-Path $OutputDirectory "camera-material-draws.csv"
$projectionCsvPath = Join-Path $OutputDirectory "camera-projection-signatures.csv"
$materialSummaryCsvPath = Join-Path $OutputDirectory "camera-material-summary.csv"
$textureCsvPath = Join-Path $OutputDirectory "camera-material-textures.csv"
$jsonPath = Join-Path $OutputDirectory "camera-material-report.json"

$overview | Export-Csv -LiteralPath $overviewCsvPath -NoTypeInformation -Encoding UTF8
$materialCameraRows | Export-Csv -LiteralPath $materialCameraCsvPath -NoTypeInformation -Encoding UTF8
$drawCameraRows | Export-Csv -LiteralPath $drawCameraCsvPath -NoTypeInformation -Encoding UTF8
$projectionGroups | Export-Csv -LiteralPath $projectionCsvPath -NoTypeInformation -Encoding UTF8
$focusMaterialSummary | Export-Csv -LiteralPath $materialSummaryCsvPath -NoTypeInformation -Encoding UTF8
$textureStats | Export-Csv -LiteralPath $textureCsvPath -NoTypeInformation -Encoding UTF8

$report = [ordered]@{
    overview = $overview
    focus_materials = @($focusMaterialSummary)
    focus_camera_rows = @($materialCameraRows.ToArray())
    projection_signatures = @($projectionGroups)
    texture_stats = @($textureStats)
}
[pscustomobject]$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic camera/material report: $jsonPath"
Write-Output $overview
