param(
    [Parameter(Mandatory = $true)]
    [string]$BaselinePhaseCsvPath,
    [Parameter(Mandatory = $true)]
    [string]$CandidatePhaseCsvPath,
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

function Get-PhaseTuple {
    param([object]$Row)

    if ($null -eq $Row) {
        return ""
    }

    return "$($Row.small_data_ptr)|$($Row.small_data_word_88)|$($Row.small_data_flag)"
}

function Get-Int64Value {
    param(
        [object]$Row,
        [string]$Name
    )

    if ($null -eq $Row) {
        return $null
    }

    $property = $Row.PSObject.Properties[$Name]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        return $null
    }

    return [int64]$property.Value
}

$baselinePath = Resolve-FullPath $BaselinePhaseCsvPath
$candidatePath = Resolve-FullPath $CandidatePhaseCsvPath

if (-not (Test-Path -LiteralPath $baselinePath)) {
    throw "Baseline phase CSV not found: $baselinePath"
}

if (-not (Test-Path -LiteralPath $candidatePath)) {
    throw "Candidate phase CSV not found: $candidatePath"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path -Parent $candidatePath) "comparison"
}

$outputRoot = Resolve-FullPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$baselineRows = @(Import-Csv -LiteralPath $baselinePath)
$candidateRows = @(Import-Csv -LiteralPath $candidatePath)
$maxCount = [Math]::Max($baselineRows.Count, $candidateRows.Count)

$indexRows = New-Object System.Collections.Generic.List[object]
for ($index = 0; $index -lt $maxCount; $index++) {
    $baseline = if ($index -lt $baselineRows.Count) { $baselineRows[$index] } else { $null }
    $candidate = if ($index -lt $candidateRows.Count) { $candidateRows[$index] } else { $null }
    $baselineInstruction = Get-Int64Value $baseline "instruction"
    $candidateInstruction = Get-Int64Value $candidate "instruction"
    $baselineTuple = Get-PhaseTuple $baseline
    $candidateTuple = Get-PhaseTuple $candidate

    $indexRows.Add([pscustomobject][ordered]@{
        event_index = $index
        baseline_instruction = if ($null -eq $baselineInstruction) { "" } else { $baselineInstruction }
        candidate_instruction = if ($null -eq $candidateInstruction) { "" } else { $candidateInstruction }
        instruction_delta = if ($null -eq $baselineInstruction -or $null -eq $candidateInstruction) { "" } else { $candidateInstruction - $baselineInstruction }
        baseline_tuple = $baselineTuple
        candidate_tuple = $candidateTuple
        tuple_match = if ($baselineTuple.Length -eq 0 -or $candidateTuple.Length -eq 0) { "" } else { $baselineTuple -eq $candidateTuple }
        baseline_timer = if ($null -eq $baseline) { "" } else { $baseline.small_data_timer }
        candidate_timer = if ($null -eq $candidate) { "" } else { $candidate.small_data_timer }
    })
}

$baselineTupleSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($row in $baselineRows) {
    [void]$baselineTupleSet.Add((Get-PhaseTuple $row))
}

$candidateTupleSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($row in $candidateRows) {
    [void]$candidateTupleSet.Add((Get-PhaseTuple $row))
}

$sharedTuples = @($baselineTupleSet | Where-Object { $candidateTupleSet.Contains($_) -and -not [string]::IsNullOrWhiteSpace($_) })
$candidateOnlyTuples = @($candidateTupleSet | Where-Object { -not $baselineTupleSet.Contains($_) -and -not [string]::IsNullOrWhiteSpace($_) })
$baselineOnlyTuples = @($baselineTupleSet | Where-Object { -not $candidateTupleSet.Contains($_) -and -not [string]::IsNullOrWhiteSpace($_) })
$matchingIndexRows = @($indexRows | Where-Object { $_.tuple_match -eq $true })

$summary = [pscustomobject][ordered]@{
    baseline_events = $baselineRows.Count
    candidate_events = $candidateRows.Count
    baseline_first_instruction = if ($baselineRows.Count -eq 0) { "" } else { $baselineRows[0].instruction }
    candidate_first_instruction = if ($candidateRows.Count -eq 0) { "" } else { $candidateRows[0].instruction }
    first_instruction_delta = if ($baselineRows.Count -eq 0 -or $candidateRows.Count -eq 0) { "" } else { ([int64]$candidateRows[0].instruction) - ([int64]$baselineRows[0].instruction) }
    baseline_last_instruction = if ($baselineRows.Count -eq 0) { "" } else { $baselineRows[-1].instruction }
    candidate_last_instruction = if ($candidateRows.Count -eq 0) { "" } else { $candidateRows[-1].instruction }
    matching_index_tuples = $matchingIndexRows.Count
    shared_tuple_count = $sharedTuples.Count
    baseline_only_tuple_count = $baselineOnlyTuples.Count
    candidate_only_tuple_count = $candidateOnlyTuples.Count
    shared_tuples = ($sharedTuples -join " ")
    baseline_only_tuples = ($baselineOnlyTuples -join " ")
    candidate_only_tuples = ($candidateOnlyTuples -join " ")
}

$indexRows | Export-Csv -LiteralPath (Join-Path $outputRoot "small-data-phase-index-comparison.csv") -NoTypeInformation
$summary | Export-Csv -LiteralPath (Join-Path $outputRoot "small-data-phase-comparison-summary.csv") -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot "small-data-phase-comparison-summary.json")

Write-Host "Wrote Sonic small-data phase comparison: $outputRoot"
