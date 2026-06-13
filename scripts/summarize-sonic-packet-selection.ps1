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

function Convert-HexToBytes {
    param([string]$Hex)

    if ([string]::IsNullOrWhiteSpace($Hex)) {
        return [byte[]]::new(0)
    }

    $clean = $Hex.Trim()
    $bytes = [byte[]]::new([Math]::Floor($clean.Length / 2))
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [byte]::Parse($clean.Substring($i * 2, 2), [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return $bytes
}

function Get-ShortHash {
    param([string]$Hex)

    $bytes = Convert-HexToBytes $Hex
    if ($bytes.Length -eq 0) {
        return ""
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $sha = $sha256.ComputeHash($bytes)
    } finally {
        $sha256.Dispose()
    }

    return (($sha[0..7] | ForEach-Object { $_.ToString("x2", [System.Globalization.CultureInfo]::InvariantCulture) }) -join "")
}

function Parse-DoubleOrNull {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $result = 0.0
    if ([double]::TryParse($Value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$result)) {
        return $result
    }

    return $null
}

function Format-Float {
    param($Value)

    if ($null -eq $Value) {
        return ""
    }

    if ([double]::IsNaN([double]$Value) -or [double]::IsInfinity([double]$Value)) {
        return ([double]$Value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }

    return ([double]$Value).ToString("0.######", [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-ColumnValue {
    param(
        $Row,
        [string]$Name
    )

    if ($Row.PSObject.Properties.Name -contains $Name) {
        return $Row.$Name
    }

    return ""
}

$tracePath = Resolve-FullPath $TraceCsvPath
if (-not (Test-Path -LiteralPath $tracePath)) {
    throw "Sonic packet-selection trace CSV not found: $tracePath"
}

$directory = Split-Path -Parent $tracePath
if ([string]::IsNullOrWhiteSpace($SummaryCsvPath)) {
    $SummaryCsvPath = Join-Path $directory "sonic-packet-selection.summary.csv"
} else {
    $SummaryCsvPath = Resolve-FullPath $SummaryCsvPath
}

if ([string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
    $SummaryJsonPath = Join-Path $directory "sonic-packet-selection.summary.json"
} else {
    $SummaryJsonPath = Resolve-FullPath $SummaryJsonPath
}

$rows = @(Import-Csv -LiteralPath $tracePath)
if ($rows.Count -eq 0) {
    throw "Sonic packet-selection trace CSV has no rows: $tracePath"
}

$summary = foreach ($row in $rows) {
    $x = Parse-DoubleOrNull $row.object_x
    $y = Parse-DoubleOrNull $row.object_y
    $z = Parse-DoubleOrNull $row.object_z
    $boundX = Format-Float (Parse-DoubleOrNull (Get-ColumnValue $row "packet_bound_x"))
    $boundY = Format-Float (Parse-DoubleOrNull (Get-ColumnValue $row "packet_bound_y"))
    $boundZ = Format-Float (Parse-DoubleOrNull (Get-ColumnValue $row "packet_bound_z"))
    $boundRadius = Get-ColumnValue $row "packet_bound_radius"
    if ([string]::IsNullOrWhiteSpace($boundRadius)) {
        $boundRadius = Get-ColumnValue $row "object_sort_depth"
    }

    [pscustomobject]@{
        instruction = [int64]$row.instruction
        pc = $row.pc
        phase = $row.phase
        lr = $row.lr
        packet_source = $row.packet_source
        packet = $row.packet
        packet_kind = $row.packet_kind
        object = $row.object
        object_kind = $row.object_kind
        stream0 = $row.stream0
        stream1 = $row.stream1
        vertex_base = $row.vertex_base
        packet_bound_xyz = "$boundX/$boundY/$boundZ"
        packet_bound_radius = Format-Float (Parse-DoubleOrNull $boundRadius)
        packet_bound_radius_word = Get-ColumnValue $row "packet_bound_radius_word"
        object_xyz = "$(Format-Float $x)/$(Format-Float $y)/$(Format-Float $z)"
        object_w = Format-Float (Parse-DoubleOrNull $row.object_w)
        cull_result = if ($row.phase -like "*cull_return") { $row.r3 } else { "" }
        r3 = $row.r3
        r27 = $row.r27
        r30 = $row.r30
        r31 = $row.r31
        packet_hash = Get-ShortHash $row.packet_bytes
        object_hash = Get-ShortHash $row.object_bytes
    }
}

$groups = @($summary |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.packet) } |
    Group-Object packet |
    ForEach-Object {
        $items = @($_.Group)
        [pscustomobject]@{
            packet = $_.Name
            count = $items.Count
            first_instruction = ($items | Select-Object -First 1).instruction
            last_instruction = ($items | Select-Object -Last 1).instruction
            phases = (($items | Select-Object -ExpandProperty phase -Unique) -join "|")
            packet_kind = ($items | Select-Object -First 1).packet_kind
            object_kind = ($items | Select-Object -First 1).object_kind
            packet_bound_xyz = ($items | Select-Object -First 1).packet_bound_xyz
            packet_bound_radius = ($items | Select-Object -First 1).packet_bound_radius
            object_xyz = ($items | Select-Object -First 1).object_xyz
            cull_results = (($items | Where-Object { $_.cull_result -ne "" } | Select-Object -ExpandProperty cull_result -Unique) -join "|")
            sources = (($items | Select-Object -ExpandProperty packet_source -Unique) -join "|")
            packet_hash = ($items | Select-Object -First 1).packet_hash
            object_hash = ($items | Select-Object -First 1).object_hash
        }
    })

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SummaryCsvPath) | Out-Null
$summary | Export-Csv -LiteralPath $SummaryCsvPath -NoTypeInformation
[pscustomobject]@{
    rowCount = $summary.Count
    packetCount = $groups.Count
    rows = @($summary)
    packets = @($groups)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $SummaryJsonPath

Write-Host "Wrote $($summary.Count) packet-selection rows to $SummaryCsvPath"
