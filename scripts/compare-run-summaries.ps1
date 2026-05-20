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

function Format-ExiChannel {
    param($ExternalInterface, [int]$Channel)

    $channels = @((Get-Value $ExternalInterface "channels" @()))
    if ($channels.Count -le $Channel) {
        return ""
    }

    $entry = $channels[$Channel]
    return "param=$(Get-Value $entry "parameter") dev=$(Get-Value $entry "selectedDevice") cmd=$(Get-Value $entry "memoryCardCommand") status=$(Get-Value $entry "memoryCardStatus") irq=$(Get-Value $entry "transferCompleteStatus")/$(Get-Value $entry "transferCompleteMask")"
}

function Format-MmioAccesses {
    param($Accesses)

    if ($null -eq $Accesses) {
        return ""
    }

    return @($Accesses | ForEach-Object {
        "$(Get-Value $_ "kind") $(Get-Value $_ "device") $(Get-Value $_ "address")=$(Get-Value $_ "value")"
    }) -join "; "
}

function Format-DiCommandHistory {
    param($Commands)

    if ($null -eq $Commands) {
        return ""
    }

    return @($Commands | Select-Object -Last 6 | ForEach-Object {
        "#$(Get-Value $_ "sequence") $(Get-Value $_ "commandName") off=$(Get-Value $_ "discOffset") len=$(Get-Value $_ "commandLength") dma=$(Get-Value $_ "dmaAddress")/$(Get-Value $_ "dmaLength") elapsed=$(Get-Value $_ "elapsedCycles") status=$(Get-Value $_ "status") irq=$(Get-Value $_ "processorInterruptPending")"
    }) -join "; "
}

$beforeExi = Get-Value $beforeData "externalInterface" $null
$afterExi = Get-Value $afterData "externalInterface" $null
Add-Change $rows "interrupts" "processorInterruptCause" (Get-Value $beforeExi "processorInterruptCause") (Get-Value $afterExi "processorInterruptCause")
Add-Change $rows "interrupts" "processorInterruptMask" (Get-Value $beforeExi "processorInterruptMask") (Get-Value $afterExi "processorInterruptMask")

$beforeDi = Get-Value $beforeData "discInterface" $null
$afterDi = Get-Value $afterData "discInterface" $null
Add-Change $rows "di" "status" (Get-Value $beforeDi "status") (Get-Value $afterDi "status")
Add-Change $rows "di" "command0" (Get-Value $beforeDi "command0") (Get-Value $afterDi "command0")
Add-Change $rows "di" "dmaAddress" (Get-Value $beforeDi "dmaAddress") (Get-Value $afterDi "dmaAddress")
Add-Change $rows "di" "dmaLength" (Get-Value $beforeDi "dmaLength") (Get-Value $afterDi "dmaLength")
Add-Change $rows "di" "commandLatencyCycles" (Get-Value $beforeDi "commandLatencyCycles") (Get-Value $afterDi "commandLatencyCycles")
Add-Change $rows "di" "commandLatencyOverrideCycles" (Get-Value $beforeDi "commandLatencyOverrideCycles") (Get-Value $afterDi "commandLatencyOverrideCycles")
Add-Change $rows "di" "hasPendingCommand" (Get-Value $beforeDi "hasPendingCommand") (Get-Value $afterDi "hasPendingCommand")
Add-Change $rows "di" "pendingCommandCycles" (Get-Value $beforeDi "pendingCommandCycles") (Get-Value $afterDi "pendingCommandCycles")
Add-Change $rows "di" "commandHistory" (Format-DiCommandHistory (Get-Value $beforeDi "commandHistory" @())) (Format-DiCommandHistory (Get-Value $afterDi "commandHistory" @()))
Add-Change $rows "di" "recentAccesses" (Format-MmioAccesses (Get-Value $beforeDi "recentAccesses" @())) (Format-MmioAccesses (Get-Value $afterDi "recentAccesses" @()))

Add-Change $rows "exi" "hasPendingExternalInterrupt" (Get-Value $beforeExi "hasPendingExternalInterrupt") (Get-Value $afterExi "hasPendingExternalInterrupt")
Add-Change $rows "exi" "channel0" (Format-ExiChannel $beforeExi 0) (Format-ExiChannel $afterExi 0)
Add-Change $rows "exi" "channel1" (Format-ExiChannel $beforeExi 1) (Format-ExiChannel $afterExi 1)

if ($rows.Count -eq 0) {
    Write-Host "No differences found."
} else {
    $rows | Format-Table -AutoSize
}
