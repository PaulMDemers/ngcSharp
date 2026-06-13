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

function Convert-ToNullableDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-ToNullableInt64 {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [int64]::Parse([string]$Value, [System.Globalization.NumberStyles]::Integer, [System.Globalization.CultureInfo]::InvariantCulture)
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

function Get-VertexKey {
    param(
        [object]$Draw,
        [object]$Vertex
    )

    return "$Draw/$Vertex"
}

$runRoot = Resolve-FullPath $RunDirectory
if (-not (Test-Path -LiteralPath $runRoot)) {
    throw "Run directory not found: $runRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $runRoot "sonic-material-source-report"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$triangleSummaryCsvPath = Join-Path $runRoot "gx-triangle-coverage.summary.csv"
$vertexCsvPath = Join-Path $runRoot "gx-vertices.csv"
$sourceMapCsvPath = Join-Path $runRoot "sonic-transform-source-map.csv"
$provenanceSummaryCsvPath = Join-Path $runRoot "sonic-vertex-provenance.summary.csv"
$materialCsvPath = Join-Path $runRoot "gx-materials.summary.csv"

foreach ($required in @($triangleSummaryCsvPath, $vertexCsvPath, $sourceMapCsvPath)) {
    if (-not (Test-CsvHasRows $required)) {
        throw "Required CSV missing or empty: $required"
    }
}

$focusTextures = @($FocusTextureAddresses | ForEach-Object { Normalize-Hex $_ })
$triangles = @(
    Import-Csv -LiteralPath $triangleSummaryCsvPath |
        Where-Object { $focusTextures -contains (Normalize-Hex ([string](Get-ObjectValue $_ "texture_address"))) }
)
$vertices = @(Import-Csv -LiteralPath $vertexCsvPath)
$sourceRows = @(Import-Csv -LiteralPath $sourceMapCsvPath)
$materials = if (Test-CsvHasRows $materialCsvPath) { @(Import-Csv -LiteralPath $materialCsvPath) } else { @() }
$provenanceSummary = if (Test-CsvHasRows $provenanceSummaryCsvPath) { @(Import-Csv -LiteralPath $provenanceSummaryCsvPath) } else { @() }

$vertexByDrawIndex = @{}
foreach ($vertex in $vertices) {
    $vertexByDrawIndex[(Get-VertexKey (Get-ObjectValue $vertex "draw_index") (Get-ObjectValue $vertex "vertex_index"))] = $vertex
}

$sourceByFifo = @{}
foreach ($row in $sourceRows) {
    $sourceByFifo[(Normalize-Hex ([string](Get-ObjectValue $row "gx_fifo_offset")))] = $row
}

$materialByTexture = @{}
foreach ($material in $materials) {
    $texture = Normalize-Hex ([string](Get-ObjectValue $material "texture_address"))
    if (-not [string]::IsNullOrWhiteSpace($texture) -and -not $materialByTexture.ContainsKey($texture)) {
        $materialByTexture[$texture] = $material
    }
}

$vertexRows = New-Object System.Collections.Generic.List[object]
foreach ($triangle in $triangles) {
    $draw = Get-ObjectValue $triangle "draw_index"
    $texture = Normalize-Hex ([string](Get-ObjectValue $triangle "texture_address"))
    foreach ($slot in @("a", "b", "c")) {
        $vertexIndex = Get-ObjectValue $triangle "vertex_$slot"
        $vertex = $vertexByDrawIndex[(Get-VertexKey $draw $vertexIndex)]
        $fifo = Normalize-Hex ([string](Get-ObjectValue $vertex "vertex_payload_offset"))
        $source = if ($sourceByFifo.ContainsKey($fifo)) { $sourceByFifo[$fifo] } else { $null }

        $vertexRows.Add([pscustomobject][ordered]@{
            texture_address = $texture
            draw_index = $draw
            triangle_index = Get-ObjectValue $triangle "triangle_index"
            triangle_vertex_slot = $slot
            vertex_index = $vertexIndex
            vertex_payload_offset = Get-ObjectValue $vertex "vertex_payload_offset"
            clip_rejected = Get-ObjectValue $vertex "clip_rejected"
            view_x = Get-ObjectValue $vertex "view_x"
            view_y = Get-ObjectValue $vertex "view_y"
            view_z = Get-ObjectValue $vertex "view_z"
            clip_w = Get-ObjectValue $vertex "clip_w"
            clip_near = Get-ObjectValue $vertex "clip_near"
            clip_far = Get-ObjectValue $vertex "clip_far"
            screen_x = Get-ObjectValue $vertex "screen_x"
            screen_y = Get-ObjectValue $vertex "screen_y"
            tex0_s = Get-ObjectValue $vertex "tex0_s"
            tex0_t = Get-ObjectValue $vertex "tex0_t"
            raw_tex0_s = Get-ObjectValue $vertex "raw_tex0_s"
            raw_tex0_t = Get-ObjectValue $vertex "raw_tex0_t"
            color = "$(Get-ObjectValue $vertex "color_r")/$(Get-ObjectValue $vertex "color_g")/$(Get-ObjectValue $vertex "color_b")/$(Get-ObjectValue $vertex "color_a")"
            source_record = Get-ObjectValue $source "source_record"
            output_index = Get-ObjectValue $source "output_index"
            source_x = Get-ObjectValue $source "source_x"
            source_y = Get-ObjectValue $source "source_y"
            source_z = Get-ObjectValue $source "source_z"
            input_index = Get-ObjectValue $source "input_index"
            input_address = Get-ObjectValue $source "input_address"
            input_x = Get-ObjectValue $source "input_x"
            input_y = Get-ObjectValue $source "input_y"
            input_z = Get-ObjectValue $source "input_z"
            transform_instruction = Get-ObjectValue $source "transform_instruction"
            transform_pc = Get-ObjectValue $source "transform_pc"
            transform_output_cursor = Get-ObjectValue $source "transform_output_cursor"
            transform_input_cursor = Get-ObjectValue $source "transform_input_cursor"
            transform_iterations = Get-ObjectValue $source "transform_iterations"
        })
    }
}

$summaryRows = @(
    foreach ($group in ($vertexRows | Group-Object texture_address)) {
        $rows = @($group.Group)
        $texture = $group.Name
        $material = if ($materialByTexture.ContainsKey($texture)) { $materialByTexture[$texture] } else { $null }
        $triangleGroup = @($triangles | Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "texture_address"))) -eq $texture })
        [pscustomobject][ordered]@{
            texture_address = $texture
            material_draws = Get-ObjectValue $material "draws"
            material_triangles = Get-ObjectValue $material "triangles"
            material_texture_format = Get-ObjectValue $material "texture_format"
            material_texture_size = Get-ObjectValue $material "texture_size"
            material_texture_filter = Get-ObjectValue $material "texture_filter"
            material_texture_lod = Get-ObjectValue $material "texture_lod"
            triangle_rows = $triangleGroup.Count
            unique_draws = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "draw_index" })
            unique_vertices = (@($rows | ForEach-Object { "$(Get-ObjectValue $_ "draw_index"):$(Get-ObjectValue $_ "vertex_index")" } | Sort-Object -Unique).Count)
            unique_source_records = (@($rows | ForEach-Object { Get-ObjectValue $_ "source_record" } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique).Count)
            source_records = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "source_record" })
            output_indices = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "output_index" })
            input_indices = Format-Unique ($rows | ForEach-Object { Get-ObjectValue $_ "input_index" })
            source_record_min = (@($rows | ForEach-Object { Get-ObjectValue $_ "source_record" } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object | Select-Object -First 1) -join "")
            source_record_max = (@($rows | ForEach-Object { Get-ObjectValue $_ "source_record" } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object | Select-Object -Last 1) -join "")
            output_index_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "output_index") })
            input_index_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "input_index") })
            input_address_min = (@($rows | ForEach-Object { Get-ObjectValue $_ "input_address" } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object | Select-Object -First 1) -join "")
            input_address_max = (@($rows | ForEach-Object { Get-ObjectValue $_ "input_address" } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object | Select-Object -Last 1) -join "")
            source_x_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "source_x") })
            source_y_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "source_y") })
            source_z_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "source_z") })
            input_x_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "input_x") })
            input_y_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "input_y") })
            input_z_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "input_z") })
            view_x_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "view_x") })
            view_y_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "view_y") })
            view_z_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "view_z") })
            tex_s_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "tex0_s") })
            tex_t_range = Format-Range ($rows | ForEach-Object { Convert-ToNullableDouble (Get-ObjectValue $_ "tex0_t") })
            material_uv_s_range = "$(Get-ObjectValue $material "uv_s_min")..$(Get-ObjectValue $material "uv_s_max")"
            material_uv_t_range = "$(Get-ObjectValue $material "uv_t_min")..$(Get-ObjectValue $material "uv_t_max")"
            clip_rejected_vertices = @($rows | Where-Object { [string](Get-ObjectValue $_ "clip_rejected") -eq "True" }).Count
            color_writes = Get-ObjectValue $material "color_writes"
            black_color_writes = Get-ObjectValue $material "black_color_writes"
            black_write_ratio = Get-ObjectValue $material "black_write_ratio"
            sample_raster_rgba_top = Get-ObjectValue $material "sample_raster_rgba_top"
            sample_tev_rgba_top = Get-ObjectValue $material "sample_tev_rgba_top"
            texture_xy_top = Get-ObjectValue $material "texture_xy_top"
            texture_mip_samples_top = Get-ObjectValue $material "texture_mip_samples_top"
        }
    }
)

$pairwiseRows = @(
    for ($leftIndex = 0; $leftIndex -lt $summaryRows.Count; $leftIndex++) {
        for ($rightIndex = $leftIndex + 1; $rightIndex -lt $summaryRows.Count; $rightIndex++) {
            $left = $summaryRows[$leftIndex]
            $right = $summaryRows[$rightIndex]
            $leftRecords = @([string](Get-ObjectValue $left "source_records") -split " " | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            $rightRecords = @([string](Get-ObjectValue $right "source_records") -split " " | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            $overlap = @($leftRecords | Where-Object { $rightRecords -contains $_ } | Sort-Object -Unique)
            [pscustomobject][ordered]@{
                left_texture_address = Get-ObjectValue $left "texture_address"
                right_texture_address = Get-ObjectValue $right "texture_address"
                left_source_record_count = $leftRecords.Count
                right_source_record_count = $rightRecords.Count
                shared_source_record_count = $overlap.Count
                shared_source_records = $overlap -join " "
                left_only_source_records = (@($leftRecords | Where-Object { $rightRecords -notcontains $_ } | Sort-Object -Unique) -join " ")
                right_only_source_records = (@($rightRecords | Where-Object { $leftRecords -notcontains $_ } | Sort-Object -Unique) -join " ")
            }
        }
    }
)

$provenanceSummaryRows = @(
    $provenanceSummary |
        Where-Object { $focusTextures.Count -eq 0 -or (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq "0x813184D0" }
)

$vertexCsvPathOut = Join-Path $OutputDirectory "material-source-vertices.csv"
$summaryCsvPath = Join-Path $OutputDirectory "material-source-summary.csv"
$pairwiseCsvPath = Join-Path $OutputDirectory "material-source-overlaps.csv"
$jsonPath = Join-Path $OutputDirectory "material-source-report.json"

$vertexRows | Export-Csv -LiteralPath $vertexCsvPathOut -NoTypeInformation -Encoding UTF8
$summaryRows | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation -Encoding UTF8
$pairwiseRows | Export-Csv -LiteralPath $pairwiseCsvPath -NoTypeInformation -Encoding UTF8

$report = [pscustomobject]([ordered]@{
    run_directory = $runRoot
    focus_textures = [object[]]$focusTextures
    material_source_summary = [object[]]$summaryRows
    material_source_overlaps = [object[]]$pairwiseRows
    material_source_vertices = [object[]]$vertexRows.ToArray()
    provenance_summary = [object[]]$provenanceSummaryRows
})

$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

Write-Host "Sonic material/source report: $jsonPath"
Write-Output $summaryRows
