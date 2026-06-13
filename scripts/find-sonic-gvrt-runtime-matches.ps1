param(
    [Parameter(Mandatory = $true)]
    [string]$GvrtContactCsvPath,
    [string]$TextureIndexRoot = "artifacts",
    [string[]]$TextureIndexCsvPaths = @(),
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

function Add-Unique {
    param(
        [System.Collections.Generic.HashSet[string]]$Set,
        [string]$Value
    )

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        [void]$Set.Add($Value)
    }
}

$gvrtFullPath = Resolve-FullPath $GvrtContactCsvPath
if (-not (Test-Path -LiteralPath $gvrtFullPath)) {
    throw "GVRT contact CSV not found: $gvrtFullPath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $gvrtFullPath) "runtime-sweep"
} else {
    $OutputDirectory = Resolve-FullPath $OutputDirectory
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$indexPaths = @()
if ($TextureIndexCsvPaths.Count -gt 0) {
    foreach ($path in $TextureIndexCsvPaths) {
        $resolved = Resolve-FullPath $path
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "Texture index CSV not found: $resolved"
        }

        $indexPaths += $resolved
    }
} else {
    $root = Resolve-FullPath $TextureIndexRoot
    if (-not (Test-Path -LiteralPath $root)) {
        throw "Texture index root not found: $root"
    }

    $indexPaths = @(
        Get-ChildItem -LiteralPath $root -Recurse -Filter index.csv |
            Where-Object { $_.FullName -match '[\\/]textures[\\/]index\.csv$' } |
            Sort-Object FullName |
            ForEach-Object { $_.FullName }
    )
}

$gvrtRows = @(Import-Csv -LiteralPath $gvrtFullPath)
$gvrtByMip0Hash = @{}
foreach ($gvrt in $gvrtRows) {
    $hash = Normalize-Hex ([string](Get-ObjectValue $gvrt "mip0_hash"))
    if ([string]::IsNullOrWhiteSpace($hash)) {
        continue
    }

    if (-not $gvrtByMip0Hash.ContainsKey($hash)) {
        $gvrtByMip0Hash[$hash] = New-Object System.Collections.ArrayList
    }

    [void]$gvrtByMip0Hash[$hash].Add($gvrt)
}

$detailRows = New-Object System.Collections.ArrayList
foreach ($indexPath in $indexPaths) {
    $indexRows = @(Import-Csv -LiteralPath $indexPath)
    foreach ($texture in $indexRows) {
        if ([string](Get-ObjectValue $texture "mip_level") -ne "0") {
            continue
        }

        $hash = Normalize-Hex ([string](Get-ObjectValue $texture "source_hash"))
        if (-not $gvrtByMip0Hash.ContainsKey($hash)) {
            continue
        }

        foreach ($gvrt in $gvrtByMip0Hash[$hash]) {
            $png = [string](Get-ObjectValue $texture "path")
            $pngFullPath = if ([System.IO.Path]::IsPathRooted($png)) {
                $png
            } elseif (-not [string]::IsNullOrWhiteSpace($png)) {
                Join-Path (Split-Path -Parent $indexPath) $png
            } else {
                ""
            }

            [void]$detailRows.Add([pscustomobject]@{
                payload_address = Get-ObjectValue $gvrt "payload_address"
                header_address = Get-ObjectValue $gvrt "header_address"
                focus_payload_delta = Get-ObjectValue $gvrt "focus_payload_delta"
                gvrt_width = Get-ObjectValue $gvrt "width"
                gvrt_height = Get-ObjectValue $gvrt "height"
                gvrt_format = Get-ObjectValue $gvrt "format_name"
                gvrt_average_luma = Get-ObjectValue $gvrt "average_luma"
                gvrt_mip0_hash = $hash
                texture_index_csv = $indexPath
                draw_index = Get-ObjectValue $texture "draw_index"
                fifo_offset = Get-ObjectValue $texture "fifo_offset"
                slot = Get-ObjectValue $texture "slot"
                texture_source_address = Get-ObjectValue $texture "source_address"
                texture_width = Get-ObjectValue $texture "width"
                texture_height = Get-ObjectValue $texture "height"
                texture_format = Get-ObjectValue $texture "format"
                texture_source_bytes = Get-ObjectValue $texture "source_bytes"
                texture_png_path = $pngFullPath
            })
        }
    }
}

$summaryRows = New-Object System.Collections.ArrayList
foreach ($gvrt in $gvrtRows) {
    $payload = Get-ObjectValue $gvrt "payload_address"
    $matches = @($detailRows | Where-Object { $_.payload_address -eq $payload })
    $draws = New-Object 'System.Collections.Generic.HashSet[string]'
    $addresses = New-Object 'System.Collections.Generic.HashSet[string]'
    $indices = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($match in $matches) {
        Add-Unique $draws ([string]$match.draw_index)
        Add-Unique $addresses ([string]$match.texture_source_address)
        Add-Unique $indices ([string]$match.texture_index_csv)
    }

    [void]$summaryRows.Add([pscustomobject]@{
        payload_address = $payload
        header_address = Get-ObjectValue $gvrt "header_address"
        focus_payload_delta = Get-ObjectValue $gvrt "focus_payload_delta"
        format_name = Get-ObjectValue $gvrt "format_name"
        width = Get-ObjectValue $gvrt "width"
        height = Get-ObjectValue $gvrt "height"
        average_luma = Get-ObjectValue $gvrt "average_luma"
        mip0_hash = Normalize-Hex ([string](Get-ObjectValue $gvrt "mip0_hash"))
        runtime_match_count = $matches.Count
        runtime_texture_addresses = ($addresses | Sort-Object) -join " "
        runtime_draws = ($draws | Sort-Object { [int]$_ }) -join " "
        texture_index_count = $indices.Count
        texture_index_paths = ($indices | Sort-Object) -join " | "
    })
}

$summaryCsvPath = Join-Path $OutputDirectory "sonic-gvrt-runtime-sweep.csv"
$detailCsvPath = Join-Path $OutputDirectory "sonic-gvrt-runtime-sweep.details.csv"
$jsonPath = Join-Path $OutputDirectory "sonic-gvrt-runtime-sweep.json"

$summaryRows | Export-Csv -LiteralPath $summaryCsvPath -NoTypeInformation -Encoding UTF8
$detailRows | Export-Csv -LiteralPath $detailCsvPath -NoTypeInformation -Encoding UTF8
[pscustomobject]@{
    gvrt_contact_csv_path = $gvrtFullPath
    texture_index_root = if ($TextureIndexCsvPaths.Count -gt 0) { "" } else { Resolve-FullPath $TextureIndexRoot }
    texture_index_csv_paths = $indexPaths
    summary = $summaryRows
    details = $detailRows
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

[pscustomobject]@{
    summary_csv = $summaryCsvPath
    details_csv = $detailCsvPath
    json = $jsonPath
    gvrt_count = $gvrtRows.Count
    texture_index_count = $indexPaths.Count
    match_count = $detailRows.Count
}
