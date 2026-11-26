$expectedFixture = 'C:\Qualification\fixture\DesktopMemoryFixture.exe'
function New-TestReport {
    [pscustomobject]@{
        SchemaVersion=1;PerMonitorDpiContext=$true;TopologyStable=$true;DesktopHintStable=$true;IncludesIdentities=$true
        DesktopsBefore=[pscustomobject]@{ActiveDesktopHint='desktop-a'}
        DesktopsAfter=[pscustomobject]@{ActiveDesktopHint='desktop-a'}
        MonitorsBefore=@([pscustomobject]@{ScalePercent=100;Bounds=[pscustomobject]@{Width=1920;Height=1080}})
        MonitorsAfter=@(
            [pscustomobject]@{ScalePercent=100;Bounds=[pscustomobject]@{Width=1920;Height=1080}}
            [pscustomobject]@{ScalePercent=125;Bounds=[pscustomobject]@{Width=1080;Height=1920}}
        )
        Windows=@([pscustomobject]@{
            ProcessName='DesktopMemoryFixture';ExecutablePath=$expectedFixture;Title='Desktop Memory Fixture - Default'
            Handle='0x1234';ProcessId=42;Classification='candidate';Minimized=$false;Maximized=$false
            Membership=[pscustomobject]@{IsCurrent=$true;DesktopId='desktop-a';DesktopHResult=0;CurrentHResult=0}
            ScreenBounds=[pscustomobject]@{Left=100;Top=100;Right=700;Bottom=600}
        })
    }
}

function Expect-Rejection([scriptblock]$Work, [string]$Message) {
    $rejected = $false
    try { & $Work } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        $rejected = $true
    }
    if (-not $rejected) { throw "Expected rejection: $Message" }
}

$a = [pscustomobject]@{CapturedUtc='2026-01-01T12:00:00Z';BootUtc='2026-01-01T10:00:00Z';ReportSha256='A'}
$b = [pscustomobject]@{CapturedUtc='2026-01-01T12:01:00Z';BootUtc='2026-01-01T10:00:00Z';ReportSha256='B'}
$exact = [pscustomobject]@{check='fixtureBounds'}
$reboot = [pscustomobject]@{check='reboot'}
$mixed = [pscustomobject]@{check='mixedDpiPortrait'}
$disconnect = [pscustomobject]@{check='disconnect'}
$first = New-TestReport
$second = New-TestReport
Test-CaseEvidence $exact $a $b $first $second $expectedFixture
Expect-Rejection { Test-CaseEvidence $reboot $a $b $first $second $expectedFixture } 'No later Windows boot'
$b.BootUtc = '2026-01-01T12:00:30Z'
Test-CaseEvidence $reboot $a $b $first $second $expectedFixture
$second.Windows[0].ScreenBounds.Left = 101
Expect-Rejection { Test-CaseEvidence $reboot $a $b $first $second $expectedFixture } 'bounds differ'
$second = New-TestReport
$second.Windows[0].ExecutablePath = 'C:\Other\DesktopMemoryFixture.exe'
Expect-Rejection { Test-CaseEvidence $reboot $a $b $first $second $expectedFixture } 'Expected exactly one'
$second = New-TestReport
$second.IncludesIdentities = $false
Expect-Rejection { Test-CaseEvidence $reboot $a $b $first $second $expectedFixture } 'requires -IncludeIdentities'
$second = New-TestReport
Test-CaseEvidence $mixed $a $b $first $second $expectedFixture
$second.MonitorsAfter[1].ScalePercent = 100
Expect-Rejection { Test-CaseEvidence $mixed $a $b $first $second $expectedFixture } 'Mixed scaling'
Expect-Rejection { Test-CaseEvidence $disconnect $a $b $first $second $expectedFixture } 'disconnection'
$second.MonitorsAfter = @($second.MonitorsAfter[0])
Test-CaseEvidence $disconnect $a $b $first $second $expectedFixture
$second.TopologyStable = $false
Expect-Rejection { Test-CaseEvidence $disconnect $a $b $first $second $expectedFixture } 'incomplete or unstable'
$second = New-TestReport
$b.ReportSha256 = 'A'
Expect-Rejection { Test-CaseEvidence $mixed $a $b $first $second $expectedFixture } 'must be distinct'
$b.ReportSha256 = 'B'
Expect-Rejection { Test-CaseEvidence $mixed $b $a $first $second $expectedFixture } 'not chronological'

$temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString('N'))
try {
    $folder = Join-Path (Join-Path $temp 'exact-reuse') 'attempt-001'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $probe = Join-Path $folder 'before.probe.json'
    Write-Json $probe $first
    $manifest = [pscustomobject]@{RunId='test';ExeSha256='exe-hash'}
    $meta = [pscustomobject]@{
        RunId='test';ExeSha256='exe-hash';Stage='before';Case='exact-reuse';Attempt=1
        ReportSha256=(Hash $probe)
    }
    Write-Json (Join-Path $folder 'before.json') $meta
    $loaded = Read-Capture $folder 'before' $manifest
    if ($loaded.Report.SchemaVersion -ne 1) { throw 'Capture did not round trip.' }
    Expect-Rejection { Write-Json $probe $first } 'Evidence already exists'
    Add-Content -LiteralPath $probe -Value ' '
    Expect-Rejection { Read-Capture $folder 'before' $manifest } 'modified'
} finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}
Write-Host 'Desktop Memory evidence self-tests passed (simulated data only).'
