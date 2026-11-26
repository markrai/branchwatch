#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Init','Capture','Record','Evaluate','SelfTest')]
    [string]$Action,
    [string]$RunId,
    [string]$Case,
    [ValidateRange(1,999)]
    [int]$Attempt = 1,
    [ValidateSet('before','after')]
    [string]$Stage,
    [ValidateSet('Pass','Fail','Blocked')]
    [string]$Result,
    [string]$Notes,
    [switch]$IncludeIdentities
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$caseFile = Join-Path $PSScriptRoot 'desktop-memory-cases.json'
$cases = @(Get-Content -LiteralPath $caseFile -Raw | ConvertFrom-Json)

function Read-Json([string]$Path) {
    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Write-Json([string]$Path, $Value) {
    if (Test-Path -LiteralPath $Path) { throw "Evidence already exists: $Path. Use a new attempt." }
    $temp = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $Value | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $temp -Encoding UTF8
        [IO.File]::Move($temp, $Path)
    } finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp }
    }
}

function Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

function Get-BootUtc {
    (Get-CimInstance Win32_OperatingSystem).LastBootUpTime.ToUniversalTime().ToString('o')
}

function Assert-Artifact($Manifest, [string]$Root) {
    if ($Manifest.SchemaVersion -ne 1 -or $Manifest.RunId -ne (Split-Path -Leaf $Root) -or
        $Manifest.UnitTests -ne 'Passed' -or $Manifest.Commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Evidence manifest identity is invalid.'
    }
    if ((Hash (Join-Path $Root 'cases.json')) -ne $Manifest.CaseHash) { throw 'Case matrix changed.' }
    $expected = @('app\BranchWatch.exe','fixture\DesktopMemoryFixture.exe')
    if (@($Manifest.Artifacts).Count -ne $expected.Count) { throw 'Artifact inventory is incomplete.' }
    foreach ($relative in $expected) {
        $entry = @($Manifest.Artifacts | Where-Object Path -eq $relative)
        if ($entry.Count -ne 1 -or (Hash (Join-Path $Root $relative)) -ne $entry[0].Sha256) {
            throw "Artifact changed or missing: $relative"
        }
    }
    if ((Hash (Join-Path $Root 'app\BranchWatch.exe')) -ne $Manifest.ExeSha256) { throw 'Application hash mismatch.' }
}

function Assert-QualificationRevision($Manifest, [string]$Root, [string]$CurrentCaseFile, [string]$Commit) {
    if ($Manifest.Commit -cne $Commit) { throw 'Qualification run belongs to a different commit. Use its exact checkout or initialize a new run.' }
    if ((Hash $CurrentCaseFile) -ne (Hash (Join-Path $Root 'cases.json'))) {
        throw 'Qualification run uses a different case matrix. Initialize a new run.'
    }
}

function Assert-RunningBuild([string]$Exe) {
    $running = @(Get-Process -Name BranchWatch -ErrorAction SilentlyContinue)
    if ($running.Count -ne 1 -or $running[0].Path -ne $Exe) {
        throw "Exit any other BranchWatch instance and start the qualification build: $Exe"
    }
}

function Get-Fixture($Report, [string]$ExpectedPath) {
    if (-not $Report.IncludesIdentities) { throw 'Fixture comparison requires -IncludeIdentities on both captures.' }
    $windows = @($Report.Windows | Where-Object {
        $_.ProcessName -eq 'DesktopMemoryFixture' -and $_.ExecutablePath -eq $ExpectedPath -and
        $_.Title -ceq 'Desktop Memory Fixture - Default' -and
        $_.Membership.IsCurrent -eq $true -and $_.Membership.DesktopHResult -ge 0 -and
        $_.Membership.CurrentHResult -ge 0 -and $_.Membership.DesktopId -eq $Report.DesktopsAfter.ActiveDesktopHint -and
        $_.Classification -eq 'candidate' -and
        -not $_.Minimized -and -not $_.Maximized -and $null -ne $_.ScreenBounds
    })
    if ($windows.Count -ne 1) { throw 'Expected exactly one normal Default fixture on the active desktop.' }
    $windows[0]
}

function Assert-Probe($Report) {
    if ($Report.SchemaVersion -ne 1 -or -not $Report.PerMonitorDpiContext -or
        -not $Report.TopologyStable -or -not $Report.DesktopHintStable -or
        @($Report.MonitorsBefore).Count -eq 0 -or @($Report.MonitorsAfter).Count -eq 0 -or
        $null -eq $Report.DesktopsBefore -or $null -eq $Report.DesktopsAfter) {
        throw 'Probe is incomplete or unstable.'
    }
}

function Test-CaseEvidence($Definition, $Before, $After, $BeforeReport, $AfterReport, [string]$ExpectedFixture) {
    Assert-Probe $BeforeReport
    Assert-Probe $AfterReport
    if ([datetimeoffset]$Before.CapturedUtc -ge [datetimeoffset]$After.CapturedUtc) {
        throw 'Before/after evidence is not chronological.'
    }
    if ($Before.ReportSha256 -eq $After.ReportSha256) { throw 'Before and after reports must be distinct.' }
    switch ($Definition.check) {
        'reboot' {
            if ([datetimeoffset]$After.BootUtc -le [datetimeoffset]$Before.BootUtc) {
                throw 'No later Windows boot was observed.'
            }
            $a = Get-Fixture $BeforeReport $ExpectedFixture
            $b = Get-Fixture $AfterReport $ExpectedFixture
            if ($a.Membership.DesktopId -ne $b.Membership.DesktopId) { throw 'Fixture desktop changed after reboot.' }
            foreach ($edge in @('Left','Top','Right','Bottom')) {
                if ($a.ScreenBounds.$edge -ne $b.ScreenBounds.$edge) { throw "Reboot fixture bounds differ at $edge." }
            }
        }
        'fixtureBounds' {
            $a = Get-Fixture $BeforeReport $ExpectedFixture
            $b = Get-Fixture $AfterReport $ExpectedFixture
            if ($a.Handle -ne $b.Handle -or $a.ProcessId -ne $b.ProcessId -or
                $a.Membership.DesktopId -ne $b.Membership.DesktopId -or $Before.BootUtc -ne $After.BootUtc) {
                throw 'Existing fixture identity changed.'
            }
            foreach ($edge in @('Left','Top','Right','Bottom')) {
                if ($a.ScreenBounds.$edge -ne $b.ScreenBounds.$edge) { throw "Fixture bounds differ at $edge." }
            }
        }
        'mixedDpiPortrait' {
            $monitors = @($AfterReport.MonitorsAfter)
            $scales = @($monitors | Where-Object { $null -ne $_.ScalePercent } | Select-Object -ExpandProperty ScalePercent -Unique)
            $portrait = @($monitors | Where-Object { $_.Bounds.Height -gt $_.Bounds.Width })
            if ($monitors.Count -lt 2 -or $scales.Count -lt 2 -or $portrait.Count -lt 1) {
                throw 'Mixed scaling and a portrait monitor were not observed.'
            }
        }
        'disconnect' {
            if (@($BeforeReport.MonitorsAfter).Count -lt 2 -or
                @($AfterReport.MonitorsAfter).Count -ge @($BeforeReport.MonitorsAfter).Count) {
                throw 'Secondary monitor disconnection was not observed.'
            }
        }
        'stable' {}
        default { throw "Unknown evidence check: $($Definition.check)" }
    }
}

function Read-Capture([string]$Folder, [string]$StageName, $Manifest) {
    $expectedCase = Split-Path -Leaf (Split-Path -Parent $Folder)
    $expectedAttempt = Split-Path -Leaf $Folder
    $metadata = Read-Json (Join-Path $Folder "$StageName.json")
    $reportPath = Join-Path $Folder "$StageName.probe.json"
    if ($metadata.RunId -ne $Manifest.RunId -or $metadata.ExeSha256 -ne $Manifest.ExeSha256 -or
        $metadata.Stage -ne $StageName -or $metadata.Case -ne $expectedCase -or
        ('attempt-{0:D3}' -f [int]$metadata.Attempt) -ne $expectedAttempt -or $metadata.ReportSha256 -ne (Hash $reportPath)) {
        throw "Invalid or modified $StageName evidence."
    }
    $report = Read-Json $reportPath
    Assert-Probe $report
    [pscustomobject]@{ Metadata = $metadata; Report = $report }
}

if ($Action -eq 'SelfTest') {
    . (Join-Path $PSScriptRoot 'desktop-memory-qualification-selftest.ps1')
    return
}

if ($env:OS -ne 'Windows_NT') { throw 'Run qualification on an interactive Windows machine.' }
if ([string]::IsNullOrWhiteSpace($RunId)) { throw 'Provide -RunId, for example dm-001.' }
if ($RunId -cnotmatch '^[a-zA-Z0-9][a-zA-Z0-9-]{0,63}$') { throw 'Invalid run ID.' }
$root = Join-Path (Join-Path $env:LOCALAPPDATA 'BranchWatch\desktop-memory-qualification') $RunId
$exe = Join-Path $root 'app\BranchWatch.exe'
$fixture = Join-Path $root 'fixture\DesktopMemoryFixture.exe'
$manifestPath = Join-Path $root 'manifest.json'

if ($Action -eq 'Init') {
    if (Test-Path -LiteralPath $root) { throw 'Run already exists. Choose a new run ID.' }
    Push-Location $repo
    try {
        $commit = (& git rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Git revision unavailable.' }
        $dirty = @(& git status --porcelain)
        if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Use a clean checkout before initializing evidence.' }
        New-Item -ItemType Directory -Path $root | Out-Null
        Invoke-Checked dotnet @('test','BranchWatch.sln','-c','Release')
        $arch = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        $rid = switch ($arch) { 'X64' {'win-x64'} 'Arm64' {'win-arm64'} default {throw "Unsupported architecture: $arch"} }
        Invoke-Checked dotnet @('publish','BranchWatch/BranchWatch.csproj','-c','Release','-r',$rid,'--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-p:DebugSymbols=false','-o',(Join-Path $root 'app'))
        Invoke-Checked dotnet @('publish','tools/DesktopMemoryFixture/DesktopMemoryFixture.csproj','-c','Release','-r',$rid,'--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-p:DebugSymbols=false','-o',(Join-Path $root 'fixture'))
        Copy-Item -LiteralPath $caseFile -Destination (Join-Path $root 'cases.json')
        $artifacts = @(
            [pscustomobject]@{Path='app\BranchWatch.exe';Sha256=(Hash $exe)}
            [pscustomobject]@{Path='fixture\DesktopMemoryFixture.exe';Sha256=(Hash $fixture)}
        )
        Write-Json $manifestPath ([ordered]@{
            SchemaVersion=1;RunId=$RunId;Commit=$commit;Architecture=$arch;Rid=$rid
            CreatedUtc=[datetimeoffset]::UtcNow.ToString('o');CaseHash=(Hash (Join-Path $root 'cases.json'))
            ExeSha256=(Hash $exe);Artifacts=$artifacts;UnitTests='Passed'
        })
    } finally { Pop-Location }
    Write-Host "Evidence: $root"
    Write-Host "Exit the installed BranchWatch instance, then run: Start-Process '$exe'"
    Write-Host "Test window: Start-Process '$fixture'"
    return
}

$manifest = Read-Json $manifestPath
Assert-Artifact $manifest $root
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Git revision unavailable.' }
$dirty = @(& git -C $repo status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Use a clean checkout of the qualification revision.' }
Assert-QualificationRevision $manifest $root $caseFile $commit
$cases = @(Read-Json (Join-Path $root 'cases.json'))

if ($Action -eq 'Evaluate') {
    $results = foreach ($definition in $cases) {
        $caseDir = Join-Path $root $definition.id
        $status = 'NotRun'
        $detail = 'No recorded attempt.'
        if (Test-Path -LiteralPath $caseDir) {
            $latest = Get-ChildItem -LiteralPath $caseDir -Directory | Where-Object { $_.Name -match '^attempt-[0-9]{3}$' } |
                Sort-Object Name -Descending | Select-Object -First 1
            if ($null -ne $latest) {
                $recordPath = Join-Path $latest.FullName 'result.json'
                $status = 'Incomplete'
                $detail = 'Latest attempt has no final result.'
                if (Test-Path -LiteralPath $recordPath) {
                    try {
                        $record = Read-Json $recordPath
                        if ($record.RunId -ne $RunId -or $record.Case -ne $definition.id -or
                            ('attempt-{0:D3}' -f [int]$record.Attempt) -ne $latest.Name -or
                            [string]::IsNullOrWhiteSpace($record.Notes)) { throw 'Invalid result identity or notes.' }
                        if ($record.Result -notin @('Pass','Fail','Blocked')) { throw 'Unknown result.' }
                        $status = $record.Result
                        $detail = $record.Notes
                        if ($status -eq 'Pass') {
                            $before = Read-Capture $latest.FullName 'before' $manifest
                            $after = Read-Capture $latest.FullName 'after' $manifest
                            if ($record.BeforeSha256 -ne $before.Metadata.ReportSha256 -or
                                $record.AfterSha256 -ne $after.Metadata.ReportSha256) { throw 'Result references different evidence.' }
                            Test-CaseEvidence $definition $before.Metadata $after.Metadata $before.Report $after.Report $fixture
                        }
                    } catch { $status = 'InvalidEvidence'; $detail = $_.Exception.Message }
                }
            }
        }
        [pscustomobject]@{Case=$definition.id;Result=$status;Detail=$detail}
    }
    $results | Format-Table -AutoSize -Wrap
    $passed = @($results | Where-Object Result -eq 'Pass').Count
    Write-Host "$passed of $($cases.Count) cases passed for commit $($manifest.Commit)."
    if ($passed -ne $cases.Count) { Write-Host 'HARDWARE GATE: NOT PASSED'; exit 1 }
    Write-Host 'HARDWARE GATE: RECORDED PASS (manual assertions plus evidence checks; review before release; no live Firefox authorization)'
    return
}

$definition = $cases | Where-Object id -eq $Case
if ($null -eq $definition) { throw "Unknown case: $Case" }
$folder = Join-Path (Join-Path $root $definition.id) ('attempt-{0:D3}' -f $Attempt)
New-Item -ItemType Directory -Path $folder -Force | Out-Null

if ($Action -eq 'Capture') {
    if (-not $Stage) { throw 'Specify -Stage before or after.' }
    Assert-RunningBuild $exe
    $source = Join-Path $env:APPDATA 'BranchWatch\desktop-memory'
    $known = @(Get-ChildItem -LiteralPath $source -Filter 'desktop-memory-probe-*.json' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
    $boot = Get-BootUtc
    $argsList = @('--desktop-memory-probe')
    if ($IncludeIdentities) { $argsList += '--include-identities'; Write-Warning 'Window titles and paths will be retained locally. Do not share raw evidence.' }
    if ((Test-Path -LiteralPath (Join-Path $folder "$Stage.json")) -or (Test-Path -LiteralPath (Join-Path $folder "$Stage.probe.json"))) {
        throw 'Capture already exists; use a new attempt.'
    }
    $p = Start-Process -FilePath $exe -ArgumentList $argsList -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Probe failed: $($p.ExitCode)" }
    $new = @(Get-ChildItem -LiteralPath $source -Filter 'desktop-memory-probe-*.json' | Where-Object {
        $_.Name -notin $known -and $_.Name -notlike '*.error.json'
    })
    if ($new.Count -ne 1) { throw 'Expected exactly one new successful probe report.' }
    $report = Read-Json $new[0].FullName
    Assert-Probe $report
    Assert-RunningBuild $exe
    if ($boot -ne (Get-BootUtc)) { throw 'Boot changed during capture.' }
    $dest = Join-Path $folder "$Stage.probe.json"
    Copy-Item -LiteralPath $new[0].FullName -Destination $dest
    Write-Json (Join-Path $folder "$Stage.json") ([ordered]@{
        RunId=$RunId;Case=$Case;Attempt=$Attempt;Stage=$Stage;CapturedUtc=[datetimeoffset]::UtcNow.ToString('o');BootUtc=$boot
        ExeSha256=$manifest.ExeSha256;ReportSha256=(Hash $dest)
    })
    Write-Host "Saved local evidence: $folder"
    return
}

if ($Action -eq 'Record') {
    if (-not $Result -or [string]::IsNullOrWhiteSpace($Notes)) { throw 'Specify -Result and factual -Notes describing the observed outcome.' }
    $beforeHash = $null
    $afterHash = $null
    if ($Result -eq 'Pass') {
        $before = Read-Capture $folder 'before' $manifest
        $after = Read-Capture $folder 'after' $manifest
        Test-CaseEvidence $definition $before.Metadata $after.Metadata $before.Report $after.Report $fixture
        $beforeHash = $before.Metadata.ReportSha256
        $afterHash = $after.Metadata.ReportSha256
    }
    Write-Json (Join-Path $folder 'result.json') ([ordered]@{
        RunId=$RunId;Case=$Case;Attempt=$Attempt;Result=$Result;Notes=$Notes
        RecordedUtc=[datetimeoffset]::UtcNow.ToString('o');BeforeSha256=$beforeHash;AfterSha256=$afterHash
    })
    Write-Host "Recorded $Case attempt $Attempt : $Result"
}
