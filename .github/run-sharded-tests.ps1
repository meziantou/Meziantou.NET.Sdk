#Requires -Version 7

<#
.SYNOPSIS
    Runs a deterministic shard of a Microsoft Testing Platform (xUnit.net v3) test
    project using 'dotnet test' in MTP mode.

.DESCRIPTION
    Discovers the tests at method granularity (theories are not pre-enumerated so
    that the shard assignment stays stable), splits them across the requested number
    of shards, and runs only the tests that belong to the current shard using exact
    '--filter-method' matches. A TRX report is written to the results directory.
#>

param(
    [Parameter(Mandatory = $true)][int]$ShardIndex,
    [Parameter(Mandatory = $true)][int]$TotalShards,
    [Parameter(Mandatory = $true)][string]$Project,
    [string]$ResultsDirectory = "test_results"
)

$ErrorActionPreference = "Stop"

if ($ShardIndex -lt 1 -or $ShardIndex -gt $TotalShards) {
    throw "ShardIndex must be between 1 and TotalShards ($TotalShards)."
}

dotnet build $Project
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

# Discover tests at method granularity (theories are not pre-enumerated).
$listOutput = dotnet test $Project --no-build --list-tests --pre-enumerate-theories off | Out-String
if ($LASTEXITCODE -ne 0) {
    Write-Host $listOutput
    throw "Test discovery failed with exit code $LASTEXITCODE."
}

# Test names are fully-qualified method names: no whitespace and at least one dot.
# This excludes every other line printed by the platform (banners, the
# "Discovered N tests." summary, build output, ...).
$tests = foreach ($line in ($listOutput -split "`r?`n")) {
    $trimmed = $line.Trim()
    if ($trimmed -and -not ($trimmed -match '\s') -and $trimmed.Contains('.')) {
        $trimmed
    }
}

$tests = $tests | Sort-Object -CaseSensitive -Unique
if ($tests.Count -eq 0) {
    throw "No tests were discovered."
}

$selected = for ($i = 0; $i -lt $tests.Count; $i++) {
    if (($i % $TotalShards) -eq ($ShardIndex - 1)) { $tests[$i] }
}

Write-Host "Shard $ShardIndex/$TotalShards selected $($selected.Count) of $($tests.Count) tests."
if ($selected.Count -eq 0) {
    exit 0
}

$filterArgs = foreach ($test in $selected) { "--filter-method"; $test }

dotnet test $Project --no-build --pre-enumerate-theories off `
    --report-xunit-trx --results-directory $ResultsDirectory @filterArgs
exit $LASTEXITCODE
