param(
    [Parameter(Mandatory = $true)]
    [string]$SourceMapCsvPath,
    [string]$OutputCsvPath = "",
    [string]$OutputJsonPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
    }

    return $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path (Get-Location) $Path))
}

function Read-DoubleOrNull {
    param(
        [object]$Row,
        [string]$Name
    )

    $value = $Row.$Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    return [double]::Parse($value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Double {
    param([double]$Value)

    return $Value.ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Solve-Linear4 {
    param(
        [double[,]]$A,
        [double[]]$B
    )

    $n = 4
    $m = New-Object 'double[,]' $n, ($n + 1)
    for ($row = 0; $row -lt $n; $row++) {
        for ($column = 0; $column -lt $n; $column++) {
            $m[$row,$column] = $A[$row,$column]
        }

        $m[$row,$n] = $B[$row]
    }

    for ($pivot = 0; $pivot -lt $n; $pivot++) {
        $best = $pivot
        $bestAbs = [Math]::Abs([double]$m.GetValue($pivot, $pivot))
        for ($row = $pivot + 1; $row -lt $n; $row++) {
            $abs = [Math]::Abs([double]$m.GetValue($row, $pivot))
            if ($abs -gt $bestAbs) {
                $best = $row
                $bestAbs = $abs
            }
        }

        if ($bestAbs -lt 1e-12) {
            throw "Singular transform fit matrix."
        }

        if ($best -ne $pivot) {
            for ($column = $pivot; $column -le $n; $column++) {
                $tmp = $m[$pivot,$column]
                $m[$pivot,$column] = $m[$best,$column]
                $m[$best,$column] = $tmp
            }
        }

        $scale = $m[$pivot,$pivot]
        for ($column = $pivot; $column -le $n; $column++) {
            $m[$pivot,$column] /= $scale
        }

        for ($row = 0; $row -lt $n; $row++) {
            if ($row -eq $pivot) {
                continue
            }

            $factor = $m[$row,$pivot]
            if ([Math]::Abs($factor) -lt 1e-18) {
                continue
            }

            for ($column = $pivot; $column -le $n; $column++) {
                $m[$row,$column] -= $factor * $m[$pivot,$column]
            }
        }
    }

    $result = New-Object 'double[]' $n
    for ($row = 0; $row -lt $n; $row++) {
        $result[$row] = $m[$row,$n]
    }

    return $result
}

function Fit-Axis {
    param(
        [object[]]$Points,
        [string]$OutputName
    )

    $normal = New-Object 'double[,]' 4, 4
    $rhs = New-Object 'double[]' 4

    foreach ($point in $Points) {
        $features = @($point.input_x, $point.input_y, $point.input_z, 1.0)
        $output = $point.$OutputName
        for ($row = 0; $row -lt 4; $row++) {
            $rhs[$row] += $features[$row] * $output
            for ($column = 0; $column -lt 4; $column++) {
                $normal[$row,$column] += $features[$row] * $features[$column]
            }
        }
    }

    return Solve-Linear4 -A $normal -B $rhs
}

function Predict {
    param(
        [double[]]$Coefficients,
        [object]$Point
    )

    return $Coefficients[0] * $Point.input_x +
        $Coefficients[1] * $Point.input_y +
        $Coefficients[2] * $Point.input_z +
        $Coefficients[3]
}

$sourceMapPath = Resolve-FullPath $SourceMapCsvPath
if (-not (Test-Path -LiteralPath $sourceMapPath)) {
    throw "Sonic transform source-map CSV not found: $sourceMapPath"
}

$directory = Split-Path -Parent $sourceMapPath
if ([string]::IsNullOrWhiteSpace($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $directory "sonic-transform-affine-fit.csv"
} else {
    $OutputCsvPath = Resolve-FullPath $OutputCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $directory "sonic-transform-affine-fit.json"
} else {
    $OutputJsonPath = Resolve-FullPath $OutputJsonPath
}

$points = @(
    Import-Csv -LiteralPath $sourceMapPath |
        ForEach-Object {
            $inputX = Read-DoubleOrNull $_ "input_x"
            $inputY = Read-DoubleOrNull $_ "input_y"
            $inputZ = Read-DoubleOrNull $_ "input_z"
            $outputX = Read-DoubleOrNull $_ "source_x"
            $outputY = Read-DoubleOrNull $_ "source_y"
            $outputZ = Read-DoubleOrNull $_ "source_z"
            if ($null -eq $inputX -or $null -eq $inputY -or $null -eq $inputZ -or
                $null -eq $outputX -or $null -eq $outputY -or $null -eq $outputZ) {
                return
            }

            [pscustomobject]@{
                packet = $_.packet
                output_index = $_.output_index
                input_index = $_.input_index
                input_x = $inputX
                input_y = $inputY
                input_z = $inputZ
                source_x = $outputX
                source_y = $outputY
                source_z = $outputZ
            }
        }
)

if ($points.Count -lt 4) {
    throw "Need at least four mapped source/output rows to fit an affine transform."
}

$xCoefficients = Fit-Axis -Points $points -OutputName "source_x"
$yCoefficients = Fit-Axis -Points $points -OutputName "source_y"
$zCoefficients = Fit-Axis -Points $points -OutputName "source_z"

$rows = @(
    [pscustomobject]@{ axis = "x"; input_x = Format-Double $xCoefficients[0]; input_y = Format-Double $xCoefficients[1]; input_z = Format-Double $xCoefficients[2]; translation = Format-Double $xCoefficients[3] },
    [pscustomobject]@{ axis = "y"; input_x = Format-Double $yCoefficients[0]; input_y = Format-Double $yCoefficients[1]; input_z = Format-Double $yCoefficients[2]; translation = Format-Double $yCoefficients[3] },
    [pscustomobject]@{ axis = "z"; input_x = Format-Double $zCoefficients[0]; input_y = Format-Double $zCoefficients[1]; input_z = Format-Double $zCoefficients[2]; translation = Format-Double $zCoefficients[3] }
)

$maxResidual = 0.0
$sumResidual = 0.0
foreach ($point in $points) {
    $dx = (Predict -Coefficients $xCoefficients -Point $point) - $point.source_x
    $dy = (Predict -Coefficients $yCoefficients -Point $point) - $point.source_y
    $dz = (Predict -Coefficients $zCoefficients -Point $point) - $point.source_z
    $residual = [Math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
    $sumResidual += $residual
    $maxResidual = [Math]::Max($maxResidual, $residual)
}

$averageResidual = $sumResidual / $points.Count
$row0Length = [Math]::Sqrt($xCoefficients[0] * $xCoefficients[0] + $xCoefficients[1] * $xCoefficients[1] + $xCoefficients[2] * $xCoefficients[2])
$row1Length = [Math]::Sqrt($yCoefficients[0] * $yCoefficients[0] + $yCoefficients[1] * $yCoefficients[1] + $yCoefficients[2] * $yCoefficients[2])
$row2Length = [Math]::Sqrt($zCoefficients[0] * $zCoefficients[0] + $zCoefficients[1] * $zCoefficients[1] + $zCoefficients[2] * $zCoefficients[2])
$row01Dot = $xCoefficients[0] * $yCoefficients[0] + $xCoefficients[1] * $yCoefficients[1] + $xCoefficients[2] * $yCoefficients[2]
$row02Dot = $xCoefficients[0] * $zCoefficients[0] + $xCoefficients[1] * $zCoefficients[1] + $xCoefficients[2] * $zCoefficients[2]
$row12Dot = $yCoefficients[0] * $zCoefficients[0] + $yCoefficients[1] * $zCoefficients[1] + $yCoefficients[2] * $zCoefficients[2]

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputCsvPath) | Out-Null
$rows | Export-Csv -LiteralPath $OutputCsvPath -NoTypeInformation

[pscustomobject]@{
    sourceMapCsvPath = $sourceMapPath
    fittedRows = $rows
    pointCount = $points.Count
    averageResidual = [Math]::Round($averageResidual, 9)
    maxResidual = [Math]::Round($maxResidual, 9)
    rowLengths = [pscustomobject]@{
        x = [Math]::Round($row0Length, 9)
        y = [Math]::Round($row1Length, 9)
        z = [Math]::Round($row2Length, 9)
    }
    rowDotProducts = [pscustomobject]@{
        xy = [Math]::Round($row01Dot, 9)
        xz = [Math]::Round($row02Dot, 9)
        yz = [Math]::Round($row12Dot, 9)
    }
    outputCsvPath = $OutputCsvPath
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath

Write-Host "Sonic transform affine fit: $OutputCsvPath"
$rows | Format-Table -AutoSize
Write-Host ("points={0} averageResidual={1} maxResidual={2}" -f $points.Count, (Format-Double $averageResidual), (Format-Double $maxResidual))
Write-Host ("rowLengths={0}/{1}/{2} rowDots={3}/{4}/{5}" -f (Format-Double $row0Length), (Format-Double $row1Length), (Format-Double $row2Length), (Format-Double $row01Dot), (Format-Double $row02Dot), (Format-Double $row12Dot))
