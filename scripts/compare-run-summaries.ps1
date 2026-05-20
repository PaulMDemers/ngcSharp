param(
    [Parameter(Mandatory = $true)]
    [string]$Before,

    [Parameter(Mandatory = $true)]
    [string]$After,

    [switch]$All
)

$ErrorActionPreference = "Stop"

function Resolve-SummaryPath {
    param([string]$Path)

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    $item = Get-Item -LiteralPath $resolved
    if ($item.PSIsContainer) {
        $candidate = Join-Path $item.FullName "run-summary.json"
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Directory does not contain run-summary.json: $($item.FullName)"
        }

        return $candidate
    }

    return $item.FullName
}

function Read-Summary {
    param([string]$Path)

    $summaryPath = Resolve-SummaryPath $Path
    [pscustomobject]@{
        path = $summaryPath
        data = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    }
}

function Get-Value {
    param($Object, [string]$Name, $Default = "")

    if ($null -eq $Object) {
        return $Default
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $Default
    }

    return $property.Value
}

function Add-Change {
    param(
        [System.Collections.Generic.List[object]]$Rows,
        [string]$Section,
        [string]$Name,
        $BeforeValue,
        $AfterValue
    )

    $beforeString = "$BeforeValue"
        $afterString = "$AfterValue"
        if ($All -or $beforeString -ne $afterString) {
            $delta = ""
        $beforeIsNumeric = $BeforeValue -is [ValueType] -or "$BeforeValue" -match "^-?\d+(\.\d+)?$"
        $afterIsNumeric = $AfterValue -is [ValueType] -or "$AfterValue" -match "^-?\d+(\.\d+)?$"
        if ($beforeIsNumeric -and $afterIsNumeric) {
            try {
                $deltaValue = [double]$AfterValue - [double]$BeforeValue
                if ([math]::Abs($deltaValue) -gt 0.000001) {
                    $delta = if ($deltaValue -gt 0) { "+$deltaValue" } else { "$deltaValue" }
                }
            } catch {
                $delta = ""
            }
        }

        $Rows.Add([pscustomobject]@{
            section = $Section
            name = $Name
            before = $beforeString
            after = $afterString
            delta = $delta
        })
    }
}

function Format-TopEntries {
    param($Entries, [string]$KeyName, [string]$CountName, [int]$Limit = 5)

    @($Entries | Select-Object -First $Limit | ForEach-Object {
        "$($_.$KeyName):$($_.$CountName)"
    }) -join "; "
}

$beforeSummary = Read-Summary $Before
$afterSummary = Read-Summary $After
$beforeData = $beforeSummary.data
$afterData = $afterSummary.data
$rows = [System.Collections.Generic.List[object]]::new()

Write-Host "Before: $($beforeSummary.path)"
Write-Host "After:  $($afterSummary.path)"
Add-Change $rows "run" "stopReason" (Get-Value $beforeData "stopReason") (Get-Value $afterData "stopReason")
Add-Change $rows "run" "pc" (Get-Value $beforeData "pc") (Get-Value $afterData "pc")
Add-Change $rows "run" "executedInstructions" (Get-Value $beforeData "executedInstructions") (Get-Value $afterData "executedInstructions")

$beforeTimings = Get-Value $beforeData "timings" $null
$afterTimings = Get-Value $afterData "timings" $null
foreach ($name in @("totalMs", "emulationMs", "postEmulationMs", "measuredDiagnosticsMs", "gxFrameDumpMs", "pcProfileMs")) {
    Add-Change $rows "timings" $name (Get-Value $beforeTimings $name) (Get-Value $afterTimings $name)
}

$beforeFastForward = Get-Value $beforeData "fastForward" $null
$afterFastForward = Get-Value $afterData "fastForward" $null
$beforeFastForwardNames = if ($null -eq $beforeFastForward) { @() } else { @($beforeFastForward.PSObject.Properties.Name) }
$afterFastForwardNames = if ($null -eq $afterFastForward) { @() } else { @($afterFastForward.PSObject.Properties.Name) }
$fastForwardNames = @(
    $beforeFastForwardNames,
    $afterFastForwardNames
) | ForEach-Object { $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique

foreach ($name in $fastForwardNames) {
    Add-Change $rows "fastForward" $name (Get-Value $beforeFastForward $name 0) (Get-Value $afterFastForward $name 0)
}

$beforePcEntries = @((Get-Value (Get-Value $beforeData "pcProfile" $null) "entries" @()))
$afterPcEntries = @((Get-Value (Get-Value $afterData "pcProfile" $null) "entries" @()))
Add-Change $rows "profile" "topPcEntries" (Format-TopEntries $beforePcEntries "pc" "count") (Format-TopEntries $afterPcEntries "pc" "count")

$beforeFilteredPcEntries = @((Get-Value (Get-Value $beforeData "pcProfileWithoutExternalInterruptLeaves" $null) "entries" @()))
$afterFilteredPcEntries = @((Get-Value (Get-Value $afterData "pcProfileWithoutExternalInterruptLeaves" $null) "entries" @()))
Add-Change $rows "profile" "filteredTopPcEntries" (Format-TopEntries $beforeFilteredPcEntries "pc" "count") (Format-TopEntries $afterFilteredPcEntries "pc" "count")

$beforeBranch = @((Get-Value $beforeData "branchSiteProfiles" @()) | ForEach-Object {
    $entries = @((Get-Value $_ "entries" @()))
    if ($entries.Count -gt 0) {
        "$(Get-Value $_ "branchSite")->$(Get-Value $entries[0] "target"):$(Get-Value $entries[0] "count")"
    }
}) -join "; "
$afterBranch = @((Get-Value $afterData "branchSiteProfiles" @()) | ForEach-Object {
    $entries = @((Get-Value $_ "entries" @()))
    if ($entries.Count -gt 0) {
        "$(Get-Value $_ "branchSite")->$(Get-Value $entries[0] "target"):$(Get-Value $entries[0] "count")"
    }
}) -join "; "
Add-Change $rows "profile" "branchTopTargets" $beforeBranch $afterBranch

$beforePcLr = @((Get-Value $beforeData "pcLrProfiles" @()) | ForEach-Object {
    $entries = @((Get-Value $_ "entries" @()))
    if ($entries.Count -gt 0) {
        "$(Get-Value $_ "pc")<-LR$(Get-Value $entries[0] "lr"):$(Get-Value $entries[0] "count")"
    }
}) -join "; "
$afterPcLr = @((Get-Value $afterData "pcLrProfiles" @()) | ForEach-Object {
    $entries = @((Get-Value $_ "entries" @()))
    if ($entries.Count -gt 0) {
        "$(Get-Value $_ "pc")<-LR$(Get-Value $entries[0] "lr"):$(Get-Value $entries[0] "count")"
    }
}) -join "; "
Add-Change $rows "profile" "pcLrTopCallers" $beforePcLr $afterPcLr

if ($rows.Count -eq 0) {
    Write-Host "No differences found."
} else {
    $rows | Format-Table -AutoSize
}
