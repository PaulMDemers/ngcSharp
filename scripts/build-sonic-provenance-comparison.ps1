param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineDirectory,
    [Parameter(Mandatory = $true)]
    [string]$CandidateDirectory,
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

function Import-OptionalCsv {
    param(
        [string]$Directory,
        [string]$Name
    )

    $path = Join-Path $Directory $Name
    if (-not (Test-Path -LiteralPath $path)) {
        return @()
    }

    return @(Import-Csv -LiteralPath $path)
}

function Get-Prop {
    param(
        [object]$Row,
        [string]$Name
    )

    if ($null -eq $Row) {
        return ""
    }

    $property = $Row.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return ""
    }

    return [string]$property.Value
}

function New-ComparisonRow {
    param(
        [string]$Kind,
        [string]$Key,
        [string]$Field,
        [object]$Baseline,
        [object]$Candidate
    )

    $baselineValue = Get-Prop $Baseline $Field
    $candidateValue = Get-Prop $Candidate $Field
    return [pscustomobject][ordered]@{
        kind = $Kind
        key = $Key
        field = $Field
        baseline = $baselineValue
        candidate = $candidateValue
        matches = ($baselineValue -eq $candidateValue)
    }
}

function Add-KeyedComparisons {
    param(
        [System.Collections.Generic.List[object]]$Rows,
        [string]$Kind,
        [object[]]$BaselineRows,
        [object[]]$CandidateRows,
        [string[]]$KeyFields,
        [string[]]$Fields
    )

    $baselineByKey = @{}
    foreach ($row in $BaselineRows) {
        $key = (($KeyFields | ForEach-Object { Get-Prop $row $_ }) -join "|")
        if (-not $baselineByKey.ContainsKey($key)) {
            $baselineByKey[$key] = $row
        }
    }

    $candidateByKey = @{}
    foreach ($row in $CandidateRows) {
        $key = (($KeyFields | ForEach-Object { Get-Prop $row $_ }) -join "|")
        if (-not $candidateByKey.ContainsKey($key)) {
            $candidateByKey[$key] = $row
        }
    }

    $keys = @($baselineByKey.Keys + $candidateByKey.Keys | Sort-Object -Unique)
    foreach ($key in $keys) {
        $baseline = if ($baselineByKey.ContainsKey($key)) { $baselineByKey[$key] } else { $null }
        $candidate = if ($candidateByKey.ContainsKey($key)) { $candidateByKey[$key] } else { $null }
        foreach ($field in $Fields) {
            $Rows.Add((New-ComparisonRow $Kind $key $field $baseline $candidate))
        }
    }
}

$baselineRoot = Resolve-FullPath $BaselineDirectory
$candidateRoot = Resolve-FullPath $CandidateDirectory

if (-not (Test-Path -LiteralPath $baselineRoot)) {
    throw "Baseline directory not found: $baselineRoot"
}

if (-not (Test-Path -LiteralPath $candidateRoot)) {
    throw "Candidate directory not found: $candidateRoot"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $candidateRoot "sonic-provenance-comparison"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$rows = New-Object System.Collections.Generic.List[object]

Add-KeyedComparisons `
    -Rows $rows `
    -Kind "packet-selection" `
    -BaselineRows (Import-OptionalCsv $baselineRoot "sonic-packet-selection.summary.csv") `
    -CandidateRows (Import-OptionalCsv $candidateRoot "sonic-packet-selection.summary.csv") `
    -KeyFields @("phase", "packet", "packet_source") `
    -Fields @("instruction", "pc", "object", "object_kind", "stream0", "stream1", "vertex_base", "packet_bound_xyz", "packet_bound_radius_word", "object_xyz", "packet_hash", "object_hash")

Add-KeyedComparisons `
    -Rows $rows `
    -Kind "scene-state" `
    -BaselineRows (Import-OptionalCsv $baselineRoot "sonic-scene-state.summary.csv") `
    -CandidateRows (Import-OptionalCsv $candidateRoot "sonic-scene-state.summary.csv") `
    -KeyFields @("packet", "object") `
    -Fields @("instruction", "packet_kind", "stream0", "stream1", "vertex_base", "object_xyz", "packet_word2_5", "resource_flag", "state_word80", "small_data_state", "mode_pointer", "current_matrix_pointer", "previous_matrix_pointer", "state_hash", "small_data_hash", "packet_hash", "object_hash")

Add-KeyedComparisons `
    -Rows $rows `
    -Kind "materials" `
    -BaselineRows (Import-OptionalCsv $baselineRoot "gx-materials.summary.csv") `
    -CandidateRows (Import-OptionalCsv $candidateRoot "gx-materials.summary.csv") `
    -KeyFields @("texture_address", "texture_format", "texture_size", "stage0_mode") `
    -Fields @("texture_filter", "texture_lod", "draw_count", "triangle_count", "covered_pixels", "black_color_writes", "black_write_ratio", "uv_s_min", "uv_s_max", "uv_t_min", "uv_t_max", "view_w_min", "view_w_max", "draws", "triangles")

Add-KeyedComparisons `
    -Rows $rows `
    -Kind "transform-source" `
    -BaselineRows (Import-OptionalCsv $baselineRoot "sonic-transform-source-map.packet-summary.csv") `
    -CandidateRows (Import-OptionalCsv $candidateRoot "sonic-transform-source-map.packet-summary.csv") `
    -KeyFields @("packet") `
    -Fields @("rows", "rows_with_input", "output_index_min", "output_index_max", "source_x", "source_y", "source_z", "input_x", "input_y", "input_z", "input_colors")

Add-KeyedComparisons `
    -Rows $rows `
    -Kind "vertex-provenance" `
    -BaselineRows (Import-OptionalCsv $baselineRoot "sonic-vertex-provenance.summary.csv") `
    -CandidateRows (Import-OptionalCsv $candidateRoot "sonic-vertex-provenance.summary.csv") `
    -KeyFields @("packet", "anchor") `
    -Fields @("packet_kind", "first_instruction", "last_instruction", "first_fifo_offset", "last_fifo_offset", "fifo_span_bytes", "rows", "unique_source_records", "stream0", "stream1", "stream_offset_min", "stream_offset_max", "source_record_min", "source_record_max", "source_x", "source_y", "source_z", "index_mismatches", "attr0_mismatches", "attr1_mismatches", "first_record_bytes", "first_source_bytes")

$comparisonCsv = Join-Path $outputRoot "sonic-provenance-comparison.csv"
$mismatchCsv = Join-Path $outputRoot "sonic-provenance-mismatches.csv"
$summaryJson = Join-Path $outputRoot "sonic-provenance-comparison-summary.json"

$rows | Export-Csv -LiteralPath $comparisonCsv -NoTypeInformation
$mismatches = @($rows | Where-Object { $_.matches -ne $true })
$mismatches | Export-Csv -LiteralPath $mismatchCsv -NoTypeInformation

$summary = [pscustomobject][ordered]@{
    baseline = $baselineRoot
    candidate = $candidateRoot
    comparison_csv = $comparisonCsv
    mismatch_csv = $mismatchCsv
    rows = $rows.Count
    mismatches = $mismatches.Count
    by_kind = @($rows | Group-Object kind | ForEach-Object {
        $groupRows = @($_.Group)
        [pscustomobject][ordered]@{
            kind = $_.Name
            rows = $groupRows.Count
            mismatches = @($groupRows | Where-Object { $_.matches -ne $true }).Count
        }
    })
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryJson
$summary | ConvertTo-Json -Depth 6
