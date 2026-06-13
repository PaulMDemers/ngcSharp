param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedPath,

    [Parameter(Mandatory = $true)]
    [string]$ActualPath,

    [int]$MaxSamples = 16
)

$expected = [System.IO.File]::ReadAllBytes((Resolve-Path $ExpectedPath))
$actual = [System.IO.File]::ReadAllBytes((Resolve-Path $ActualPath))
$limit = [Math]::Min($expected.Length, $actual.Length)
$mismatches = 0
$samples = New-Object System.Collections.Generic.List[object]

for ($index = 0; $index -lt $limit; $index++) {
    if ($expected[$index] -eq $actual[$index]) {
        continue
    }

    $mismatches++
    if ($samples.Count -lt $MaxSamples) {
        $samples.Add([pscustomobject]@{
            Offset = ('0x{0:X}' -f $index)
            Expected = ('0x{0:X2}' -f $expected[$index])
            Actual = ('0x{0:X2}' -f $actual[$index])
        })
    }
}

$lengthDifference = [Math]::Abs($expected.Length - $actual.Length)
$totalDifference = $mismatches + $lengthDifference

[pscustomobject]@{
    ExpectedPath = (Resolve-Path $ExpectedPath).Path
    ActualPath = (Resolve-Path $ActualPath).Path
    ExpectedLength = $expected.Length
    ActualLength = $actual.Length
    ComparedLength = $limit
    ByteMismatches = $mismatches
    LengthDifference = $lengthDifference
    TotalDifference = $totalDifference
    Match = $totalDifference -eq 0
}

if ($samples.Count -gt 0) {
    $samples
}
