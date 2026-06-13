param(
    [Parameter(Mandatory = $true)]
    [string]$TraceCsvPath,
    [string]$SummaryCsvPath = "",
    [string]$SummaryJsonPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Convert-HexBytesToFloats {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return @()
    }

    $clean = $Hex.Trim()
    $floatCount = [Math]::Floor($clean.Length / 8)
    $floats = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $floatCount; $i++) {
        $word = [uint32]::Parse($clean.Substring($i * 8, 8), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
        $bytes = [BitConverter]::GetBytes($word)
        $floats.Add([double][BitConverter]::ToSingle($bytes, 0))
    }

    return $floats.ToArray()
}

function Get-ValueOrNull {
    param(
        [double[]]$Values,
        [int]$Index
    )

    if ($Index -lt 0 -or $Index -ge $Values.Length) {
        return $null
    }

    return $Values[$Index]
}

function Format-Number {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Vector {
    param([object[]]$Values)

    return (($Values | ForEach-Object { Format-Number $_ }) -join "/")
}

function Get-MatrixRows {
    param([double[]]$Values)

    return @{
        x = @((Get-ValueOrNull $Values 0), (Get-ValueOrNull $Values 1), (Get-ValueOrNull $Values 2), (Get-ValueOrNull $Values 3))
        y = @((Get-ValueOrNull $Values 4), (Get-ValueOrNull $Values 5), (Get-ValueOrNull $Values 6), (Get-ValueOrNull $Values 7))
        z = @((Get-ValueOrNull $Values 8), (Get-ValueOrNull $Values 9), (Get-ValueOrNull $Values 10), (Get-ValueOrNull $Values 11))
    }
}

function Get-Length3 {
    param([object[]]$Row)

    if ($null -eq $Row[0] -or $null -eq $Row[1] -or $null -eq $Row[2]) {
        return $null
    }

    return [Math]::Sqrt(([double]$Row[0] * [double]$Row[0]) + ([double]$Row[1] * [double]$Row[1]) + ([double]$Row[2] * [double]$Row[2]))
}

function Get-Dot3 {
    param(
        [object[]]$A,
        [object[]]$B
    )

    if ($null -eq $A[0] -or $null -eq $A[1] -or $null -eq $A[2] -or $null -eq $B[0] -or $null -eq $B[1] -or $null -eq $B[2]) {
        return $null
    }

    return ([double]$A[0] * [double]$B[0]) + ([double]$A[1] * [double]$B[1]) + ([double]$A[2] * [double]$B[2])
}

function Add-MatrixFields {
    param(
        [System.Collections.IDictionary]$Target,
        [string]$Prefix,
        [string]$Hex
    )

    $rows = Get-MatrixRows (Convert-HexBytesToFloats $Hex)
    $Target["${Prefix}_x"] = Format-Vector $rows.x
    $Target["${Prefix}_y"] = Format-Vector $rows.y
    $Target["${Prefix}_z"] = Format-Vector $rows.z
    $Target["${Prefix}_translation"] = Format-Vector @($rows.x[3], $rows.y[3], $rows.z[3])
    $Target["${Prefix}_len_x"] = Format-Number (Get-Length3 $rows.x)
    $Target["${Prefix}_len_y"] = Format-Number (Get-Length3 $rows.y)
    $Target["${Prefix}_len_z"] = Format-Number (Get-Length3 $rows.z)
    $Target["${Prefix}_dot_xy"] = Format-Number (Get-Dot3 $rows.x $rows.y)
    $Target["${Prefix}_dot_xz"] = Format-Number (Get-Dot3 $rows.x $rows.z)
    $Target["${Prefix}_dot_yz"] = Format-Number (Get-Dot3 $rows.y $rows.z)
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Sonic root-matrix trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "sonic-root-matrix.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-root-matrix.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Sonic root-matrix trace CSV has no rows: $tracePath"
}

$summary = foreach ($row in $rows) {
    $fields = [ordered]@{
        instruction = [int64]$row.instruction
        pc = $row.pc
        phase = $row.phase
        lr = $row.lr
        store_address = $row.store_address
        r3 = $row.r3
        r4 = $row.r4
        r5 = $row.r5
        r6 = $row.r6
    }

    Add-MatrixFields $fields "left" $row.left_matrix_bytes
    Add-MatrixFields $fields "right" $row.right_matrix_bytes
    Add-MatrixFields $fields "output" $row.output_matrix_bytes
    Add-MatrixFields $fields "root" $row.root_matrix_bytes
    $fields["disassembly"] = $row.disassembly
    [pscustomobject]$fields
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

$multiplyTerminals = @($summary | Where-Object { $_.phase -eq "multiply_terminal" })
$scalarTerminals = @($summary | Where-Object { $_.phase -eq "scalar_terminal" })
Write-Host "Wrote $($summary.Count) root-matrix rows to $SummaryCsvPath"
Write-Host "Multiply terminal rows: $($multiplyTerminals.Count)"
Write-Host "Scalar terminal rows: $($scalarTerminals.Count)"
