param(
    [Parameter(Mandatory = $true)]
    [string]$PacketTimelineCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$MaterialSummaryCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$GvrtContactCsvPath,
    [string]$TextureBindCsvPath = "",
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0",
    [string]$FocusTextureAddress = "0x0072C600",
    [string]$FocusPayloadAddress = "0x8137DFA0"
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Normalize-Hex {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "0x{0:X8}" -f ([uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
        )
    }

    return "0x{0:X8}" -f ([uint32]::Parse($trimmed, [System.Globalization.CultureInfo]::InvariantCulture))
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

function Select-NearestBind {
    param(
        [object[]]$Rows,
        [int]$DrawFifoStart
    )

    if ($Rows.Count -eq 0) {
        return $null
    }

    return $Rows |
        Sort-Object @{
            Expression = {
                $text = ([string]$_.gx_fifo_offset).Trim()
                if ($text.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
                    $value = [int]::Parse($text.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
                } else {
                    $value = [int]::Parse($text, [System.Globalization.CultureInfo]::InvariantCulture)
                }

                [Math]::Abs($DrawFifoStart - $value)
            }
        } |
        Select-Object -First 1
}

function Convert-FifoToInt {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return 0
    }

    $text = $Value.Trim()
    if ($text.StartsWith("+", [System.StringComparison]::Ordinal)) {
        $text = $text.Substring(1)
    }

    if ($text.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [int]::Parse($text.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [int]::Parse($text, [System.Globalization.CultureInfo]::InvariantCulture)
}

$packetTimelineFullPath = Resolve-FullPath $PacketTimelineCsvPath
$materialFullPath = Resolve-FullPath $MaterialSummaryCsvPath
$gvrtFullPath = Resolve-FullPath $GvrtContactCsvPath
foreach ($path in @($packetTimelineFullPath, $materialFullPath, $gvrtFullPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required CSV not found: $path"
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $packetTimelineFullPath) "sonic-bridge-object-material-identity"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$focusPacket = Normalize-Hex $FocusPacket
$focusTexture = Normalize-Hex $FocusTextureAddress
$focusPayload = Normalize-Hex $FocusPayloadAddress

$packetRows = @(Import-Csv -LiteralPath $packetTimelineFullPath)
$materialRows = @(Import-Csv -LiteralPath $materialFullPath)
$gvrtRows = @(Import-Csv -LiteralPath $gvrtFullPath)
$bindRows = @()
if (-not [string]::IsNullOrWhiteSpace($TextureBindCsvPath)) {
    $bindFullPath = Resolve-FullPath $TextureBindCsvPath
    if (Test-Path -LiteralPath $bindFullPath) {
        $bindRows = @(Import-Csv -LiteralPath $bindFullPath)
    }
}

$packet = $packetRows | Where-Object { (Normalize-Hex ([string]$_.packet)) -eq $focusPacket } | Select-Object -First 1
$material = $materialRows | Where-Object { (Normalize-Hex ([string]$_.texture_address)) -eq $focusTexture } | Sort-Object { [int64]$_.color_writes } -Descending | Select-Object -First 1
$gvrt = $gvrtRows | Where-Object { (Normalize-Hex ([string]$_.payload_address)) -eq $focusPayload } | Select-Object -First 1
$textureBindCandidates = @($bindRows | Where-Object { (Normalize-Hex ([string]$_.source_address)) -eq $focusTexture })
$drawFifo = Convert-FifoToInt ([string](Get-ObjectValue $packet "mapped_fifo_start"))
$bind = Select-NearestBind $textureBindCandidates $drawFifo

$row = [pscustomobject]@{
    packet = $focusPacket
    packet_kind = Get-ObjectValue $packet "packet_kind"
    object = Get-ObjectValue $packet "object"
    object_kind = Get-ObjectValue $packet "object_kind"
    object_xyz = Get-ObjectValue $packet "object_xyz"
    matrix_translation = Get-ObjectValue $packet "matrix_translation"
    stream0 = Get-ObjectValue $packet "stream0"
    stream1 = Get-ObjectValue $packet "stream1"
    packet_instruction = Get-ObjectValue $packet "instruction"
    matrix_instruction = Get-ObjectValue $packet "matrix_instruction"
    anchor_fifo_offset = Get-ObjectValue $packet "anchor_fifo_offset"
    mapped_draw_start = Get-ObjectValue $packet "mapped_draw_start"
    mapped_draw_end = Get-ObjectValue $packet "mapped_draw_end"
    mapped_fifo_start = Get-ObjectValue $packet "mapped_fifo_start"
    mapped_fifo_end = Get-ObjectValue $packet "mapped_fifo_end"
    decoded_vertices = Get-ObjectValue $packet "decoded_vertices"
    clipped_vertices = Get-ObjectValue $packet "clipped_vertices"
    view_bounds = Get-ObjectValue $packet "view_bounds"
    screen_bounds = Get-ObjectValue $packet "screen_bounds"
    texture_address = $focusTexture
    texture_format = Get-ObjectValue $material "texture_format"
    texture_size = Get-ObjectValue $material "texture_size"
    texture_filter = Get-ObjectValue $material "texture_filter"
    texture_lod = Get-ObjectValue $material "texture_lod"
    material_draws = Get-ObjectValue $material "draws"
    material_triangles = Get-ObjectValue $material "triangles"
    material_covered_pixels = Get-ObjectValue $material "covered_pixels"
    material_color_writes = Get-ObjectValue $material "color_writes"
    material_black_color_writes = Get-ObjectValue $material "black_color_writes"
    material_black_write_ratio = Get-ObjectValue $material "black_write_ratio"
    material_uv_s_min = Get-ObjectValue $material "uv_s_min"
    material_uv_s_max = Get-ObjectValue $material "uv_s_max"
    material_uv_t_min = Get-ObjectValue $material "uv_t_min"
    material_uv_t_max = Get-ObjectValue $material "uv_t_max"
    material_sample_tev_rgba_top = Get-ObjectValue $material "sample_tev_rgba_top"
    gvrt_header_address = Get-ObjectValue $gvrt "header_address"
    gvrt_payload_address = Get-ObjectValue $gvrt "payload_address"
    gvrt_payload_hash = Get-ObjectValue $gvrt "payload_hash"
    gvrt_mip0_hash = Get-ObjectValue $gvrt "mip0_hash"
    gvrt_average_luma = Get-ObjectValue $gvrt "average_luma"
    gvrt_nonblack_percent = Get-ObjectValue $gvrt "nonblack_percent"
    gvrt_runtime_match_count = Get-ObjectValue $gvrt "runtime_match_count"
    gvrt_runtime_draws = Get-ObjectValue $gvrt "runtime_draws"
    texture_bind_instruction = Get-ObjectValue $bind "instruction"
    texture_bind_fifo_offset = Get-ObjectValue $bind "gx_fifo_offset"
    texture_bind_pc = Get-ObjectValue $bind "pc"
    texture_bind_lr = Get-ObjectValue $bind "lr"
    texture_bind_texture_object = Get-ObjectValue $bind "texture_object"
    texture_bind_sampler_object = Get-ObjectValue $bind "sampler_object"
    texture_bind_mode0 = Get-ObjectValue $bind "mode0"
    texture_bind_mode1 = Get-ObjectValue $bind "mode1"
}

$csvPath = Join-Path $OutputDirectory "sonic-bridge-object-material-identity.csv"
$jsonPath = Join-Path $OutputDirectory "sonic-bridge-object-material-identity.json"
$row | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    packet_timeline_csv_path = $packetTimelineFullPath
    material_summary_csv_path = $materialFullPath
    gvrt_contact_csv_path = $gvrtFullPath
    texture_bind_csv_path = if ($bindRows.Count -gt 0) { (Resolve-FullPath $TextureBindCsvPath) } else { "" }
    identity = $row
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    csv = $csvPath
    json = $jsonPath
    packet = $focusPacket
    texture = $focusTexture
    gvrt_payload = $focusPayload
    material_draws = $row.material_draws
}
