param(
    [string]$CompatRoot = "artifacts/compat-runs",
    [string]$OutputDirectory = "",
    [string]$FocusPacket = "0x813184D0",
    [string]$FocusTextureAddress = "0x0072C600",
    [string]$FocusPayloadAddress = "0x8137DFA0",
    [string]$GvrtContactCsvPath = ""
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
        return "0x{0:X8}" -f ([uint32]::Parse($trimmed.Substring(2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture))
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

function Find-RunJsonFiles {
    param([string]$Root)

    return @(
        Get-ChildItem -LiteralPath $Root -Recurse -Filter run.json |
            Sort-Object FullName |
            ForEach-Object { $_.FullName }
    )
}

function Get-TextureHashMatches {
    param(
        [string]$TextureIndexPath,
        [string]$Mip0Hash
    )

    if ([string]::IsNullOrWhiteSpace($TextureIndexPath) -or -not (Test-Path -LiteralPath $TextureIndexPath) -or [string]::IsNullOrWhiteSpace($Mip0Hash)) {
        return @()
    }

    return @(
        Import-Csv -LiteralPath $TextureIndexPath |
            Where-Object {
                [string](Get-ObjectValue $_ "mip_level") -eq "0" -and
                (Normalize-Hex ([string](Get-ObjectValue $_ "source_hash"))) -eq $Mip0Hash
            }
    )
}

$compatRootFullPath = Resolve-FullPath $CompatRoot
if (-not (Test-Path -LiteralPath $compatRootFullPath)) {
    throw "Compatibility root not found: $compatRootFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $compatRootFullPath "sonic-bridge-phase-report"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$focusPacket = Normalize-Hex $FocusPacket
$focusTexture = Normalize-Hex $FocusTextureAddress
$focusPayload = Normalize-Hex $FocusPayloadAddress

$gvrtFocus = $null
if (-not [string]::IsNullOrWhiteSpace($GvrtContactCsvPath)) {
    $gvrtFullPath = Resolve-FullPath $GvrtContactCsvPath
    if (-not (Test-Path -LiteralPath $gvrtFullPath)) {
        throw "GVRT contact CSV not found: $gvrtFullPath"
    }

    $gvrtFocus = Import-Csv -LiteralPath $gvrtFullPath |
        Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "payload_address"))) -eq $focusPayload } |
        Select-Object -First 1
}

$focusMip0Hash = Normalize-Hex ([string](Get-ObjectValue $gvrtFocus "mip0_hash"))
$rows = New-Object System.Collections.ArrayList

foreach ($runJsonPath in Find-RunJsonFiles $compatRootFullPath) {
    $targetRoot = Split-Path -Parent $runJsonPath
    $run = Read-JsonFile $runJsonPath
    if ($null -eq $run) {
        continue
    }

    $materialCsvPath = [string](Get-ObjectValue $run "gxMaterialSummaryCsvPath")
    if ([string]::IsNullOrWhiteSpace($materialCsvPath)) {
        $materialCsvPath = Join-Path $targetRoot "gx-materials.summary.csv"
    }

    $packetTimelinePath = [string](Get-ObjectValue $run "sonicPacketTimelinePath")
    if ([string]::IsNullOrWhiteSpace($packetTimelinePath)) {
        $packetTimelinePath = Join-Path $targetRoot "sonic-packet-timeline.csv"
    }

    $textureIndexPath = [string](Get-ObjectValue $run "gxTextureIndexPath")
    if ([string]::IsNullOrWhiteSpace($textureIndexPath)) {
        $candidate = Join-Path $targetRoot "textures\index.csv"
        if (Test-Path -LiteralPath $candidate) {
            $textureIndexPath = $candidate
        }
    }

    $material = $null
    if (Test-CsvHasRows $materialCsvPath) {
        $material = Import-Csv -LiteralPath $materialCsvPath |
            Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "texture_address"))) -eq $focusTexture } |
            Sort-Object { [int64](Get-ObjectValue $_ "color_writes" 0) } -Descending |
            Select-Object -First 1
    }

    $packet = $null
    if (Test-CsvHasRows $packetTimelinePath) {
        $packet = Import-Csv -LiteralPath $packetTimelinePath |
            Where-Object { (Normalize-Hex ([string](Get-ObjectValue $_ "packet"))) -eq $focusPacket } |
            Select-Object -First 1
    }

    $textureMatches = Get-TextureHashMatches $textureIndexPath $focusMip0Hash
    $textureDraws = @($textureMatches | ForEach-Object { [string](Get-ObjectValue $_ "draw_index") } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object { [int]$_ })
    $textureAddresses = @($textureMatches | ForEach-Object { [string](Get-ObjectValue $_ "source_address") } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

    $emulatorSummary = Get-ObjectValue $run "emulatorSummary" $null
    $timings = Get-ObjectValue $emulatorSummary "timings" $null
    $copySummary = Get-ObjectValue $run "gxCopySummary" $null
    $coverageSummary = Get-ObjectValue $run "gxCoverageSummary" $null
    $frame = Get-ObjectValue $run "frame" $null

    [void]$rows.Add([pscustomobject]@{
        run_root = $targetRoot
        run_group = Split-Path -Leaf (Split-Path -Parent $targetRoot)
        target = Get-ObjectValue $run "target"
        status = Get-ObjectValue $run "status"
        max_instructions = Get-ObjectValue $run "maxInstructions"
        elapsed_seconds = Get-ObjectValue $run "elapsedSeconds"
        executed_instructions = Get-ObjectValue $emulatorSummary "executedInstructions"
        stop_reason = Get-ObjectValue $emulatorSummary "stopReason"
        pc = Get-ObjectValue $emulatorSummary "pc"
        total_ms = Get-ObjectValue $timings "totalMs"
        emulation_ms = Get-ObjectValue $timings "emulationMs"
        diagnostics_ms = Get-ObjectValue $timings "measuredDiagnosticsMs"
        focus_packet = $focusPacket
        packet_present = if ($null -eq $packet) { "False" } else { "True" }
        object = Get-ObjectValue $packet "object"
        object_kind = Get-ObjectValue $packet "object_kind"
        object_xyz = Get-ObjectValue $packet "object_xyz"
        matrix_translation = Get-ObjectValue $packet "matrix_translation"
        mapped_draw_start = Get-ObjectValue $packet "mapped_draw_start"
        mapped_draw_end = Get-ObjectValue $packet "mapped_draw_end"
        mapped_fifo_start = Get-ObjectValue $packet "mapped_fifo_start"
        decoded_vertices = Get-ObjectValue $packet "decoded_vertices"
        clipped_vertices = Get-ObjectValue $packet "clipped_vertices"
        screen_bounds = Get-ObjectValue $packet "screen_bounds"
        focus_texture = $focusTexture
        material_present = if ($null -eq $material) { "False" } else { "True" }
        material_draws = Get-ObjectValue $material "draws"
        material_triangles = Get-ObjectValue $material "triangles"
        material_color_writes = Get-ObjectValue $material "color_writes"
        material_black_writes = Get-ObjectValue $material "black_color_writes"
        material_black_ratio = Get-ObjectValue $material "black_write_ratio"
        material_uv_s_min = Get-ObjectValue $material "uv_s_min"
        material_uv_s_max = Get-ObjectValue $material "uv_s_max"
        material_uv_t_min = Get-ObjectValue $material "uv_t_min"
        material_uv_t_max = Get-ObjectValue $material "uv_t_max"
        sample_tev_rgba_top = Get-ObjectValue $material "sample_tev_rgba_top"
        focus_payload = $focusPayload
        focus_mip0_hash = $focusMip0Hash
        texture_index_path = $textureIndexPath
        gvrt_texture_match_count = $textureMatches.Count
        gvrt_texture_addresses = $textureAddresses -join " "
        gvrt_texture_draws = $textureDraws -join " "
        nonblack_display_copies = Get-ObjectValue $copySummary "nonblackDisplayCopies"
        max_display_nonblack = Get-ObjectValue $copySummary "maxDisplayNonblack"
        max_display_nonblack_percent = Get-ObjectValue $copySummary "maxDisplayNonblackPercent"
        coverage_total_color_writes = Get-ObjectValue $coverageSummary "totalColorWrites"
        coverage_total_black_writes = Get-ObjectValue $coverageSummary "totalBlackWrites"
        coverage_max_after_nonblack = Get-ObjectValue $coverageSummary "maxAfterNonblack"
        coverage_max_after_nonblack_draw = Get-ObjectValue $coverageSummary "maxAfterNonblackDraw"
        frame_path = Get-ObjectValue $frame "path"
        run_json_path = $runJsonPath
    })
}

$csvPath = Join-Path $OutputDirectory "sonic-bridge-phase-report.csv"
$jsonPath = Join-Path $OutputDirectory "sonic-bridge-phase-report.json"
$rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    compat_root = $compatRootFullPath
    focus_packet = $focusPacket
    focus_texture = $focusTexture
    focus_payload = $focusPayload
    focus_mip0_hash = $focusMip0Hash
    generated_at = (Get-Date).ToString("o")
    rows = $rows
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    csv = $csvPath
    json = $jsonPath
    rows = $rows.Count
    material_rows = @($rows | Where-Object { $_.material_present -eq "True" }).Count
    packet_rows = @($rows | Where-Object { $_.packet_present -eq "True" }).Count
    gvrt_texture_matches = @($rows | Where-Object { [int]$_.gvrt_texture_match_count -gt 0 }).Count
}
