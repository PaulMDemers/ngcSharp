param(
    [Parameter(Mandatory = $true)]
    [string]$SourceMapCsvPath,
    [string]$GroupBy = "packet",
    [string]$OutputCsvPath = "",
    [string]$OutputJsonPath = "",
    [int]$MinimumPoints = 4
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

function New-FitRow {
    param(
        [string]$GroupName,
        [object[]]$Points
    )

    if ($Points.Count -lt $MinimumPoints) {
        return [pscustomobject]@{
            group = $GroupName
            point_count = $Points.Count
            status = "too-few-points"
        }
    }

    try {
        $x = Fit-Axis -Points $Points -OutputName "source_x"
        $y = Fit-Axis -Points $Points -OutputName "source_y"
        $z = Fit-Axis -Points $Points -OutputName "source_z"
    } catch {
        return [pscustomobject]@{
            group = $GroupName
            point_count = $Points.Count
            status = $_.Exception.Message
        }
    }

    $maxResidual = 0.0
    $sumResidual = 0.0
    foreach ($point in $Points) {
        $dx = (Predict -Coefficients $x -Point $point) - $point.source_x
        $dy = (Predict -Coefficients $y -Point $point) - $point.source_y
        $dz = (Predict -Coefficients $z -Point $point) - $point.source_z
        $residual = [Math]::Sqrt($dx * $dx + $dy * $dy + $dz * $dz)
        $sumResidual += $residual
        $maxResidual = [Math]::Max($maxResidual, $residual)
    }

    $row0Length = [Math]::Sqrt($x[0] * $x[0] + $x[1] * $x[1] + $x[2] * $x[2])
    $row1Length = [Math]::Sqrt($y[0] * $y[0] + $y[1] * $y[1] + $y[2] * $y[2])
    $row2Length = [Math]::Sqrt($z[0] * $z[0] + $z[1] * $z[1] + $z[2] * $z[2])
    $row01Dot = $x[0] * $y[0] + $x[1] * $y[1] + $x[2] * $y[2]
    $row02Dot = $x[0] * $z[0] + $x[1] * $z[1] + $x[2] * $z[2]
    $row12Dot = $y[0] * $z[0] + $y[1] * $z[1] + $y[2] * $z[2]

    [pscustomobject]@{
        group = $GroupName
        point_count = $Points.Count
        status = "ok"
        average_residual = [Math]::Round($sumResidual / $Points.Count, 9)
        max_residual = [Math]::Round($maxResidual, 9)
        row0 = "$(Format-Double $x[0])/$(Format-Double $x[1])/$(Format-Double $x[2])/$(Format-Double $x[3])"
        row1 = "$(Format-Double $y[0])/$(Format-Double $y[1])/$(Format-Double $y[2])/$(Format-Double $y[3])"
        row2 = "$(Format-Double $z[0])/$(Format-Double $z[1])/$(Format-Double $z[2])/$(Format-Double $z[3])"
        row_lengths = "$([Math]::Round($row0Length, 9))/$([Math]::Round($row1Length, 9))/$([Math]::Round($row2Length, 9))"
        row_dots = "$([Math]::Round($row01Dot, 9))/$([Math]::Round($row02Dot, 9))/$([Math]::Round($row12Dot, 9))"
    }
}

$sourceMapPath = Resolve-FullPath $SourceMapCsvPath
if (-not (Test-Path -LiteralPath $sourceMapPath)) {
    throw "Sonic transform source-map CSV not found: $sourceMapPath"
}

$directory = Split-Path -Parent $sourceMapPath
if ([string]::IsNullOrWhiteSpace($OutputCsvPath)) {
    $OutputCsvPath = Join-Path $directory "sonic-transform-affine-fit-groups.csv"
} else {
    $OutputCsvPath = Resolve-FullPath $OutputCsvPath
}

if ([string]::IsNullOrWhiteSpace($OutputJsonPath)) {
    $OutputJsonPath = Join-Path $directory "sonic-transform-affine-fit-groups.json"
} else {
    $OutputJsonPath = Resolve-FullPath $OutputJsonPath
}

$rows = @(Import-Csv -LiteralPath $sourceMapPath)
if ($rows.Count -eq 0) {
    throw "Sonic transform source-map CSV has no rows: $sourceMapPath"
}

if (-not ($rows[0].PSObject.Properties.Name -contains $GroupBy)) {
    throw "Group column '$GroupBy' not found in source map."
}

$points = @(
    $rows |
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
                group = $_.$GroupBy
                input_x = $inputX
                input_y = $inputY
                input_z = $inputZ
                source_x = $outputX
                source_y = $outputY
                source_z = $outputZ
            }
        }
)

$fitRows = @(
    $points |
        Group-Object group |
        Sort-Object { $_.Group[0].group } |
        ForEach-Object { New-FitRow -GroupName $_.Name -Points @($_.Group) }
)

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputCsvPath) | Out-Null
$fitRows | Export-Csv -LiteralPath $OutputCsvPath -NoTypeInformation

[pscustomobject]@{
    sourceMapCsvPath = $sourceMapPath
    outputCsvPath = $OutputCsvPath
    groupBy = $GroupBy
    groupCount = $fitRows.Count
    pointsWithInput = $points.Count
    fits = $fitRows
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputJsonPath

Write-Host "Grouped Sonic transform affine fits: $OutputCsvPath"
$fitRows | Format-Table -AutoSize
