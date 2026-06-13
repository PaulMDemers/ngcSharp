param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath
)

$rows = Import-Csv -Path $TraceCsvPath
$summary = foreach ($row in $rows) {
    $targetBytes = [string]$row.target_output_bytes
    [pscustomobject]@{
        Instruction = [int]$row.instruction
        Pc = $row.pc
        Lr = $row.lr
        Source = $row.source
        SourceEnd = $row.source_end
        Destination = $row.destination
        OutputLength = $row.output_length
        TargetAddress = $row.target_address
        TargetLength = $row.target_length
        TargetOutputOffset = $row.target_output_offset
        SkippedInstructions = $row.skipped_instructions
        TargetBytesPrefix = if ($targetBytes.Length -gt 64) { $targetBytes.Substring(0, 64) } else { $targetBytes }
    }
}

$summaryPath = [System.IO.Path]::ChangeExtension($TraceCsvPath, ".summary.csv")
$jsonPath = [System.IO.Path]::ChangeExtension($TraceCsvPath, ".summary.json")
$summary | Export-Csv -NoTypeInformation -Path $summaryPath
$summary | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonPath
$summary
