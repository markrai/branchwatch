[CmdletBinding()]
param(
    [ValidateSet('Sample', 'Compare', 'SelfTest')]
    [string]$Action = 'Sample',
    [ValidateRange(0, 2147483647)]
    [int]$ProcessId = 0,
    [string]$Scenario,
    [string]$Workload,
    [ValidateRange(2, 3601)]
    [int]$SampleCount = 31,
    [ValidateRange(1, 60)]
    [int]$IntervalSeconds = 2,
    [string]$OutputPath,
    [string]$BaselinePath,
    [string]$CandidatePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Initialize-GuiCounters {
    if (-not ('BranchWatchMemory.GuiCounters' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace BranchWatchMemory
{
    public static class GuiCounters
    {
        [DllImport("user32.dll")]
        public static extern uint GetGuiResources(IntPtr process, uint flags);
    }
}
'@
    }
}

function Get-SampleSeries {
    param([System.Diagnostics.Process]$Process, [int]$Count, [int]$Interval)
    Initialize-GuiCounters
    $processHandle = $Process.Handle
    $started = $Process.StartTime.ToUniversalTime()
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $Count; $i++) {
        if ($i -gt 0) { Start-Sleep -Seconds $Interval }
        $Process.Refresh()
        if ($Process.HasExited -or $Process.StartTime.ToUniversalTime() -ne $started) {
            throw 'The sampled process exited or changed identity. No complete profile was collected.'
        }
        [pscustomobject]@{
            elapsedSeconds = $clock.Elapsed.TotalSeconds
            timestampUtc = [DateTimeOffset]::UtcNow.ToString('o')
            workingSetBytes = $Process.WorkingSet64
            privateBytes = $Process.PrivateMemorySize64
            peakWorkingSetBytes = $Process.PeakWorkingSet64
            handles = $Process.HandleCount
            threads = $Process.Threads.Count
            gdiObjects = [BranchWatchMemory.GuiCounters]::GetGuiResources($processHandle, 0)
            userObjects = [BranchWatchMemory.GuiCounters]::GetGuiResources($processHandle, 1)
            cpuSeconds = $Process.TotalProcessorTime.TotalSeconds
        }
    }
    $Process.Refresh()
    if ($Process.HasExited) { throw 'The sampled process exited before collection completed.' }
}

function Get-Statistics {
    param([double[]]$Values)
    if ($Values.Count -eq 0) { throw 'No samples.' }
    $sorted = @($Values | Sort-Object)
    $n = $sorted.Count
    $middle = [int][math]::Floor($n / 2)
    $median = if ($n % 2 -eq 0) {
        ($sorted[$middle - 1] + $sorted[$middle]) / 2.0
    } else { $sorted[$middle] }
    [pscustomobject]@{
        first = $Values[0]
        last = $Values[-1]
        min = $sorted[0]
        median = $median
        p95 = $sorted[[int][math]::Ceiling($n * 0.95) - 1]
        max = $sorted[-1]
        delta = $Values[-1] - $Values[0]
    }
}

function Get-Summary {
    param([object[]]$Samples)
    if ($Samples.Count -lt 2) { throw 'At least two samples are required.' }
    $duration = $Samples[-1].elapsedSeconds - $Samples[0].elapsedSeconds
    $cpu = $Samples[-1].cpuSeconds - $Samples[0].cpuSeconds
    if ($duration -le 0 -or $cpu -lt 0) { throw 'Invalid elapsed time or CPU counter.' }
    $summary = [ordered]@{}
    foreach ($metric in @('workingSetBytes', 'privateBytes', 'peakWorkingSetBytes', 'handles', 'threads', 'gdiObjects', 'userObjects')) {
        $values = @($Samples | ForEach-Object { [double]$_.$metric })
        if (@($values | Where-Object { $_ -lt 0 -or [double]::IsNaN($_) -or [double]::IsInfinity($_) }).Count -gt 0) {
            throw "Invalid values for $metric."
        }
        $summary[$metric] = Get-Statistics -Values $values
    }
    $summary['durationSeconds'] = $duration
    $summary['cpuSecondsDelta'] = $cpu
    $summary['cpuPercentOfOneLogicalCore'] = 100.0 * $cpu / $duration
    [pscustomobject]$summary
}

function Get-Comparison {
    param($Baseline, $Candidate)
    foreach ($profile in @($Baseline, $Candidate)) {
        if ($profile.schemaVersion -ne 1 -or $profile.samples.Count -ne $profile.sampleCount) {
            throw 'Unsupported or incomplete profile.'
        }
    }
    foreach ($field in @('scenario', 'workload', 'sampleCount', 'intervalSeconds', 'osVersion', 'osArchitecture', 'logicalProcessors')) {
        if ($Baseline.$field -cne $Candidate.$field) {
            throw "Profiles differ in $field. Collect matching scenarios and sampling conditions."
        }
    }
    $before = Get-Summary -Samples $Baseline.samples
    $after = Get-Summary -Samples $Candidate.samples
    foreach ($metric in @('privateBytes', 'workingSetBytes', 'handles', 'threads', 'gdiObjects', 'userObjects')) {
        $old = $before.$metric.median
        $new = $after.$metric.median
        $scale = if ($metric.EndsWith('Bytes')) { 1MB } else { 1 }
        $unit = if ($scale -eq 1MB) { 'MiB' } else { 'count' }
        [pscustomobject]@{
            Metric = "$metric ($unit, median)"
            Baseline = [math]::Round($old / $scale, 3)
            Candidate = [math]::Round($new / $scale, 3)
            Delta = [math]::Round(($new - $old) / $scale, 3)
            PercentChange = if ($old -ne 0) { [math]::Round(100.0 * ($new - $old) / $old, 2) } else { $null }
        }
    }
    $old = $before.cpuPercentOfOneLogicalCore
    $new = $after.cpuPercentOfOneLogicalCore
    [pscustomobject]@{
        Metric = 'CPU (% of one logical core)'
        Baseline = [math]::Round($old, 3)
        Candidate = [math]::Round($new, 3)
        Delta = [math]::Round($new - $old, 3)
        PercentChange = if ($old -ne 0) { [math]::Round(100.0 * ($new - $old) / $old, 2) } else { $null }
    }
}

function Assert-ProfileTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Self-test failed: $Message" }
}

function New-TestProfile {
    param([long]$Offset = 0, [string]$Label = 'idle')
    $samples = @(for ($i = 0; $i -lt 4; $i++) {
        [pscustomobject]@{
            elapsedSeconds = $i * 2
            workingSetBytes = (100 + $i * 2) * 1MB + $Offset
            privateBytes = (80 + $i * 2) * 1MB + $Offset
            peakWorkingSetBytes = 120 * 1MB
            handles = 100 + $i
            threads = 8
            gdiObjects = 4
            userObjects = 5
            cpuSeconds = $i * 0.1
        }
    })
    [pscustomobject]@{
        schemaVersion = 1
        scenario = $Label
        workload = 'fixture'
        sampleCount = 4
        intervalSeconds = 2
        osVersion = 'test'
        osArchitecture = 'X64'
        logicalProcessors = 8
        samples = $samples
    }
}

if ($Action -eq 'SelfTest') {
    $fixture = New-TestProfile
    $summary = Get-Summary $fixture.samples
    Assert-ProfileTest ($summary.privateBytes.median -eq 83MB) 'even median'
    Assert-ProfileTest ($summary.privateBytes.p95 -eq 86MB) 'nearest-rank percentile'
    Assert-ProfileTest ($summary.privateBytes.delta -eq 6MB) 'first to last delta'
    Assert-ProfileTest ([math]::Abs($summary.cpuPercentOfOneLogicalCore - 5) -lt 0.0001) 'CPU interval normalization'
    $candidate = New-TestProfile -Offset (-10MB)
    $comparison = @(Get-Comparison $fixture $candidate)
    Assert-ProfileTest ($comparison[0].Delta -eq -10) 'private memory comparison'
    $roundtrip = $fixture | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    Assert-ProfileTest (@(Get-Comparison $fixture $roundtrip)[0].Delta -eq 0) 'JSON round trip'
    $rejected = $false
    try { $null = @(Get-Comparison $fixture (New-TestProfile -Label 'other')) }
    catch { $rejected = $true }
    Assert-ProfileTest $rejected 'mismatched scenario'
    $rejected = $false
    $candidate.sampleCount = 5
    try { $null = @(Get-Comparison $fixture $candidate) }
    catch { $rejected = $true }
    Assert-ProfileTest $rejected 'incomplete profile'
    if ($IsWindows) {
        $self = Get-Process -Id $PID
        try {
            $live = @(Get-SampleSeries -Process $self -Count 2 -Interval 1)
            $liveSummary = Get-Summary $live
            Assert-ProfileTest ($live.Count -eq 2 -and $liveSummary.privateBytes.median -gt 0) 'live Windows counters'
        } finally { $self.Dispose() }
    }
    Write-Host 'Memory profile self-tests passed.'
    return
}

if ($Action -eq 'Compare') {
    if (-not $BaselinePath -or -not $CandidatePath) { throw 'Specify -BaselinePath and -CandidatePath.' }
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
    $candidate = Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json
    Get-Comparison $baseline $candidate | Format-Table -AutoSize
    return
}

if (-not $IsWindows) { throw 'Sampling BranchWatch requires PowerShell 7 on Windows.' }
if ([string]::IsNullOrWhiteSpace($Scenario) -or [string]::IsNullOrWhiteSpace($Workload)) {
    throw 'Specify -Scenario and -Workload (non-sensitive workload identifier).'
}
$processes = if ($ProcessId -gt 0) { @(Get-Process -Id $ProcessId) } else { @(Get-Process -Name BranchWatch) }
try {
    if ($processes.Count -ne 1 -or $processes[0].ProcessName -ine 'BranchWatch') {
        throw 'Select exactly one running BranchWatch process using -ProcessId.'
    }
    $process = $processes[0]
    $exe = $process.Path
    if ([string]::IsNullOrWhiteSpace($exe)) { throw 'Cannot identify the running executable.' }
    $start = $process.StartTime.ToUniversalTime()
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
    if (-not $OutputPath) {
        $root = Join-Path $env:LOCALAPPDATA 'BranchWatch/memory-profiles'
        $OutputPath = Join-Path $root ("profile-{0}-{1}.json" -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff'), $process.Id)
    }
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    if (Test-Path -LiteralPath $OutputPath) { throw 'Output already exists. Choose a new path.' }
    Write-Host "Collecting $SampleCount samples for '$Scenario' from PID $($process.Id). Keep the workload unchanged."
    $samples = @(Get-SampleSeries -Process $process -Count $SampleCount -Interval $IntervalSeconds)
    if ($samples.Count -ne $SampleCount -or $process.StartTime.ToUniversalTime() -ne $start) {
        throw 'Incomplete profile or process identity changed.'
    }
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $hash) {
        throw 'Executable changed during sampling.'
    }
    $summary = Get-Summary $samples
    $profile = [ordered]@{
        schemaVersion = 1
        scenario = $Scenario
        workload = $Workload
        sampleCount = $SampleCount
        intervalSeconds = $IntervalSeconds
        processId = $process.Id
        processStartedUtc = $start.ToString('o')
        executableSha256 = $hash
        executableVersion = $fileVersion
        osVersion = [Environment]::OSVersion.VersionString
        osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        logicalProcessors = [Environment]::ProcessorCount
        samples = $samples
        summary = $summary
    }
    $null = [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($OutputPath))
    $json = $profile | ConvertTo-Json -Depth 8
    $stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew)
    try {
        $writer = [System.IO.StreamWriter]::new($stream)
        try { $writer.WriteLine($json) } finally { $writer.Dispose() }
    } finally { $stream.Dispose() }
    [pscustomobject]@{
        Scenario = $Scenario
        WorkingSetMedianMiB = [math]::Round($summary.workingSetBytes.median / 1MB, 2)
        PrivateMedianMiB = [math]::Round($summary.privateBytes.median / 1MB, 2)
        PrivateP95MiB = [math]::Round($summary.privateBytes.p95 / 1MB, 2)
        PrivateDeltaMiB = [math]::Round($summary.privateBytes.delta / 1MB, 2)
        HandlesMedian = $summary.handles.median
        ThreadsMedian = $summary.threads.median
        GdiObjectsMedian = $summary.gdiObjects.median
        UserObjectsMedian = $summary.userObjects.median
        CpuPercentOfOneLogicalCore = [math]::Round($summary.cpuPercentOfOneLogicalCore, 3)
        Profile = $OutputPath
    } | Format-List
} finally {
    foreach ($item in $processes) { $item.Dispose() }
}
