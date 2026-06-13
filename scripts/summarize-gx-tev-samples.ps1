param(
    [Parameter(Mandatory = $true)]
    [string]$TevSampleCsvPath,
    [string]$SummaryCsvPath = "",
    [string]$JsonPath = ""
)

$ErrorActionPreference = "Stop"

function Convert-ToDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 0.0
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Convert-OptionalDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    return [double]::Parse([string]$Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-Luma {
    param([string]$Color)

    if ([string]::IsNullOrWhiteSpace($Color) -or $Color -notmatch '^\s*(\d+)\/(\d+)\/(\d+)\/(\d+)') {
        return $null
    }

    $r = [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
    $g = [double]::Parse($Matches[2], [System.Globalization.CultureInfo]::InvariantCulture)
    $b = [double]::Parse($Matches[3], [System.Globalization.CultureInfo]::InvariantCulture)
    return ($r + $g + $b) / 3.0
}

function Get-StageTextureLuma {
    param([string]$StageSummary)

    if ([string]::IsNullOrWhiteSpace($StageSummary) -or $StageSummary -notmatch 'texture=(\d+)\/(\d+)\/(\d+)\/(\d+)') {
        return $null
    }

    $r = [double]::Parse($Matches[1], [System.Globalization.CultureInfo]::InvariantCulture)
    $g = [double]::Parse($Matches[2], [System.Globalization.CultureInfo]::InvariantCulture)
    $b = [double]::Parse($Matches[3], [System.Globalization.CultureInfo]::InvariantCulture)
    return ($r + $g + $b) / 3.0
}

function Get-TextureAddress {
    param([string]$TextureState)

    if ([string]::IsNullOrWhiteSpace($TextureState) -or $TextureState -notmatch 'addr=(0x[0-9A-Fa-f]+)') {
        return ""
    }

    return $Matches[1].ToUpperInvariant()
}

function Get-FirstNonEmpty {
    param([object[]]$Values)

    foreach ($value in $Values) {
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            return [string]$value
        }
    }

    return ""
}

if (-not (Test-Path -LiteralPath $TevSampleCsvPath)) {
    throw "TEV sample CSV not found: $TevSampleCsvPath"
}

if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = [IO.Path]::ChangeExtension($TevSampleCsvPath, ".summary.csv")
}

if ([string]::IsNullOrWhiteSpace($JsonPath)) {
    $JsonPath = [IO.Path]::ChangeExtension($TevSampleCsvPath, ".summary.json")
}

$rows = @(Import-Csv -LiteralPath $TevSampleCsvPath)
$summaryRows = New-Object System.Collections.Generic.List[object]

foreach ($group in ($rows | Group-Object draw_index)) {
    $drawRows = @($group.Group)
    $rasterLuma = @($drawRows | ForEach-Object { Get-Luma $_.raster_rgba } | Where-Object { $null -ne $_ })
    $textureLuma = @($drawRows | ForEach-Object { Get-StageTextureLuma $_.stage_summary } | Where-Object { $null -ne $_ })
    $tevLuma = @($drawRows | ForEach-Object { Get-Luma $_.tev_rgba } | Where-Object { $null -ne $_ })
    $selectedLod = @($drawRows | ForEach-Object { Convert-OptionalDouble $_.selected_texture_lod } | Where-Object { $null -ne $_ })
    $blackTev = @($tevLuma | Where-Object { $_ -le 0.5 })
    $summaryRows.Add([pscustomobject]@{
        draw_index = [int]::Parse($group.Name, [System.Globalization.CultureInfo]::InvariantCulture)
        samples = $drawRows.Count
        fifo_offset = Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.fifo_offset })
        stage0_mode = Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.stage0_mode })
        tev_stage_count = Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.tev_stage_count })
        indirect_stage_count = Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.indirect_stage_count })
        gen_mode = Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.gen_mode })
        selected_texture_lod_avg = if ($selectedLod.Count -eq 0) { "" } else { [Math]::Round(($selectedLod | Measure-Object -Average).Average, 3) }
        selected_texture_lod_min = if ($selectedLod.Count -eq 0) { "" } else { [Math]::Round(($selectedLod | Measure-Object -Minimum).Minimum, 3) }
        selected_texture_lod_max = if ($selectedLod.Count -eq 0) { "" } else { [Math]::Round(($selectedLod | Measure-Object -Maximum).Maximum, 3) }
        texture_address = Get-TextureAddress (Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.texture_map0_state }))
        raster_luma_avg = if ($rasterLuma.Count -eq 0) { "" } else { [Math]::Round(($rasterLuma | Measure-Object -Average).Average, 3) }
        texture_luma_avg = if ($textureLuma.Count -eq 0) { "" } else { [Math]::Round(($textureLuma | Measure-Object -Average).Average, 3) }
        tev_luma_avg = if ($tevLuma.Count -eq 0) { "" } else { [Math]::Round(($tevLuma | Measure-Object -Average).Average, 3) }
        tev_black_sample_ratio = if ($tevLuma.Count -eq 0) { "" } else { [Math]::Round($blackTev.Count / [double]$tevLuma.Count, 3) }
        tev_state = Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.tev_state })
        texture_map0_state = Get-FirstNonEmpty ($drawRows | ForEach-Object { $_.texture_map0_state })
    }) | Out-Null
}

$summaryDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($SummaryCsvPath))
if (-not [string]::IsNullOrEmpty($summaryDirectory)) {
    New-Item -ItemType Directory -Force -Path $summaryDirectory | Out-Null
}

$summaryRows |
    Sort-Object draw_index |
    Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation -Encoding UTF8

[pscustomobject]@{
    source = (Resolve-Path -LiteralPath $TevSampleCsvPath).Path
    rows = $rows.Count
    draws = $summaryRows.Count
    summaries = @($summaryRows.ToArray() | Sort-Object draw_index)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $JsonPath -Encoding UTF8

[pscustomobject]@{
    csv = (Resolve-Path -LiteralPath $SummaryCsvPath).Path
    json = (Resolve-Path -LiteralPath $JsonPath).Path
    rows = $summaryRows.Count
}
