# BranchWatch memory optimization

## M.0: establish a repeatable baseline

The purpose is to reduce genuinely retained memory, native resources and idle CPU use without changing behavior or weakening native desktop or browser authorization. Do not force garbage collection, trim the working set, change alarm deadlines, reduce watcher reliability or tune runtime limits merely to improve Task Manager numbers.

The initial user observation was 197.59 MiB working set, 115.09 MiB private bytes, 199.52 MiB peak working set, 19 threads, 1,359 handles and 79.6875 seconds cumulative CPU time. This was a single uncontrolled observation, not a reproducible baseline. The earlier approximate 72 MB Task Manager reading may represent a different metric or workload.

### Collect a profile

Use PowerShell 7 in an interactive Windows session, from the repository root. The script observes a running BranchWatch process; it does not launch, modify, trim or terminate it. Use an ordinary non-elevated user session and disposable data for tests involving notes or snapshots. Keep the tested configuration stable during each sampling window.

```powershell
pwsh -File scripts/memory-profile.ps1 -Scenario current-idle -Workload current-config -OutputPath "$env:LOCALAPPDATA/BranchWatch/memory-profiles/m0-current-idle.json"
```

By default, collect 31 observations two seconds apart (approximately one minute). Use -ProcessId to select a specific instance, -SampleCount and -IntervalSeconds to alter sampling. Wait at least 60 seconds after startup or the last interaction for steady-state scenarios unless deliberately measuring a transition. The script refuses to overwrite an existing output file or return a complete profile when the process exits during collection. When no output path is specified, it creates a timestamped JSON file in the local BranchWatch memory-profiles directory.

The JSON contains raw samples, executable SHA-256, file version, process identity and start time, OS version/architecture and logical CPU count, workload and scenario identifiers, medians, nearest-rank p95, minima, maxima and first-to-last deltas. Use non-sensitive workload labels; repository paths, note contents, snapshot data and browser URLs are not collected.

### Interpret the metrics

| Metric | Meaning |
| --- | --- |
| Private bytes | Private committed process memory, including managed and native allocations; primary footprint comparison metric, not the size of the managed heap. |
| Working set | Physical pages currently resident, including shareable pages; depends on OS memory pressure. Task Manager may display private working set instead. |
| Peak working set | Lifetime high-water mark, not current memory. |
| Handles, threads, GDI and USER objects | Resource counts that help diagnose retained watchers, windows, graphics resources or threads. A zero GUI count can mean no objects or an unavailable counter. |
| CPU | Difference in cumulative processor seconds divided by elapsed seconds, expressed as a percentage of one logical core; can exceed 100 percent. Not a measurement of instantaneous CPU or wakeup counts. |

These are process-level counters, not allocation attribution. If memory remains unexplained, use an external managed heap profiler and Windows native allocation/handle tooling in separate runs; do not compare instrumented workloads with uninstrumented ones.

### Workload matrix

Keep the Windows build, machine, executable architecture, monitor topology/DPI, virtual desktop count, repository/notes fixtures, user settings, build configuration and warm-up timing constant across candidate comparisons. Record them locally. The script checks scenario/workload labels, sampling parameters, OS version/architecture and logical CPU count, but cannot establish that the actual workloads or machines match.

| Scenario | Setup |
| --- | --- |
| current-idle | Existing configuration, unchanged and idle; record settings and fixture counts. |
| tray-only | Fresh launch, overlays disabled, no open notes, settings closed, pinned mode with no repo. |
| overlays-on | Same configuration with both overlays visible and a fixed pinned repository. |
| settings-open | Open General settings; separate scenarios for each visited section, especially Desktop Memory. |
| settings-closed | Close settings after visiting all pages; compare with an equivalent fresh idle run. |
| notes-10 | Ten fixed-size transparent notes on the same desktop; no pending alarms. |
| notes-closed | Close the notes explicitly; compare against the equivalent pre-notes state. |
| workspace | Fixed workspace and repository count, file activity enabled. |
| pinned-after-workspace | Return from workspace to a fixed pinned repository. |
| idle-long | Stable workload, 31 observations spaced 60 seconds apart (30 minutes). |

For each scenario, capture at least three independent runs of the baseline and candidate; report run-to-run variation and use medians rather than cherry-picking the lowest sample. For transition and repeated-open/close experiments also compare final values and trends in private bytes and resource counts. No reduction is a success if the equivalent functional or security regression checks fail.

### Compare

Use the exact same labels and sampling parameters for candidate and baseline, different output paths, and the same physical machine/workload.

```powershell
pwsh -File scripts/memory-profile.ps1 -Action Compare -BaselinePath "$env:LOCALAPPDATA/BranchWatch/memory-profiles/m0-current-idle.json" -CandidatePath "$env:LOCALAPPDATA/BranchWatch/memory-profiles/m1-current-idle.json"
```

Negative deltas indicate lower resource use. Byte values are displayed in MiB; the JSON retains raw bytes. CPU uses percentage points in the Delta column. The comparison recalculates summaries from raw samples rather than trusting the stored summary. Do not treat a lower working set alone as proof of reclamation, or the sample p95 as a statistical confidence interval.

### Validation

```powershell
pwsh -File scripts/memory-profile.ps1 -Action SelfTest
```

Self-tests cover statistics, CPU normalization, comparison deltas, JSON round trips, mismatched/incomplete profiles, and live Windows process-counter sampling. The Windows workflow executes them without needing a desktop fixture or a running BranchWatch instance. Interactive BranchWatch baseline profiles must be collected separately.

## Roadmap

| Phase | Scope | Status |
| --- | --- | --- |
| M.0 | Repeatable profiling, baseline and comparison | Merged; Windows CI passed; interactive baseline pending |
| M.1 | Lazy overlay window creation and resource release | Merged; Windows CI passed; physical measurements pending |
| M.2 | Lazy settings pages and snapshot loading | Merged; Windows CI passed; physical measurements pending |
| M.3 | Demand-driven timers and background activity | Merged; Windows CI passed; physical measurements pending |
| M.4 | Filesystem watcher consolidation and reuse | Merged; Windows CI passed; physical measurements pending |
| M.5 | Sticky-note rendering and shared resources | Merged; Windows CI passed; physical measurements pending |
| M.6 | Controlled runtime/GC experiments only if evidence warrants | Comparison tooling implemented; Windows CI and physical measurements pending |
| M.7 | Final repeat profiling and functional regression qualification | Planned |

Start with private-byte and resource-count improvements in equivalent workloads; set numerical targets only after actual controlled baseline data. Retain the R.8 maintainability and physical qualification boundaries. No runtime flags, application behavior, storage format or dependencies change in M.0.

## M.1: overlay window lifetime

Git and virtual desktop overlays now use a shared UI-thread-owned OverlayWindowHost. Startup does not allocate either window until eligible for display. The Git overlay is eligible when enabled and the repository root is available; the virtual desktop overlay is eligible when its existing visibility setting is enabled. Disabling either closes its WPF window and releases the host reference, including native window resources; enabling reconstructs it using current settings and current repository or desktop state. Enabled refreshes reuse the same instance. Closing a window independently also clears the reference, and a failed initial render releases its candidate. Application shutdown and controller disposal do not recreate a closed overlay.

Desktop monitoring remains active at the same cadence for sticky notes and settings even when the desktop overlay is disabled. Its visual refreshes coalesce through UiRefreshCoordinator, read the latest state, and do not queue while disabled. Closing the taskbar overlay stops its 400 ms z-order timer through the existing OnClosed cleanup. No change to repository watching, click-through styles, topmost policy, persisted visibility, alarms or runtime flags.

Five Windows STA regressions cover disabled allocation, reuse and recreation from fresh state, external close, failed initial render and virtual desktop timer cleanup. Windows CI must pass. Native window placement, focus, desktop switches and physical resource measurements remain unqualified.

Measure tray-only (both overlays disabled), overlays-on, and repeated hide/show with matching process age and workload against M.0. For repeated transitions, compare equivalent steady states, including after returning to disabled. A lower private-byte median or stable handle/GDI/USER counts across repeated cycles is more meaningful than a transient working-set drop. If neither overlay is disabled, substantial savings from this slice should not be expected. No measured MiB savings claimed before collecting comparable profiles. M.2 targets lazy settings page construction.

## M.2: lazy settings pages

The SettingsWindow shell retains the existing navigation, General and About content. The Git appearance, Virtual Desktop appearance, Desktop Memory and Sticky Notes controls are constructed only when their respective sections are first visited. Their existing ContentControl hosts own the instances, avoiding duplicate page references. Desktop Memory snapshot loading, browser authorization session creation, and initial desktop discovery are consequently deferred until that section is opened. External settings refreshes and monitor notifications do not instantiate an unvisited page.

Visited pages remain attached until the window closes, preserving sticky-note appearance drafts and unsaved Desktop Memory captures across navigation. Existing visibility-based cancellation and confirmation guards are unchanged. Closing Settings unsubscribes the desktop monitor, stops an instantiated memory page before removing its content, clears all four hosts and desktop color rows, and prevents subsequent navigation or refresh from recreating content. An in-flight async operation can retain its page until completion; the existing stopped/cancellation guards still apply. No forced GC or heap trimming, storage schema, version, permissions, polling, or visual changes.

Two additional Windows STA tests cover unvisited-page allocation, current settings on first visit, external refresh, page reuse, and shutdown detachment with stopped Desktop Memory refresh. Existing navigation and draft preservation tests remain in place. M.1 merged with passing Windows CI; M.2 CI and physical profiling are pending.

Compare equivalent General-only, individual section, all-sections-visited and settings-closed workloads with M.0/M.1 using the profiling harness. M.2 principally reduces the incremental cost of opening Settings without visiting every section; it is not expected to significantly reduce fresh tray-only memory. A first visit may take longer because construction is deferred. No numeric savings claimed before matched measurements. M.3 will address timers and background activity.

## M.3: demand-driven maintenance

StickyNotesController now schedules its one-second maintenance timer only when at least one note window exists, a non-triggered alarm is pending (including closed notes), a desktop restore is pending, or dirty changes need persistence. It stops after the last obligation clears and resumes when new work arrives. Empty desktop switches no longer schedule a redundant restore tick. Failed saves keep retrying, and failed restore attempts retain the existing three-attempt limit. Alarm deadlines remain UTC and the existing one-second cadence remains while an alarm is pending; this intentionally preserves prompt detection after resume and wall-clock changes. Open notes continue periodic native affinity verification and passive body maintenance. Desktop monitoring remains at its existing 300 ms cadence, independently of overlay visibility, for restoration and affinity-sensitive features.

The virtual desktop taskbar overlay reuses its active 400 ms z-order timer across visual refreshes, rather than stopping and recreating it. Switching away from taskbar positioning or closing the overlay stops that timer. No storage schema, version, dependencies, permissions, runtime configuration, alarm or affinity-policy changes.

Four STA regression tests cover idle startup, dirty wakeup, closed-note alarm latch/reschedule/dismissal, save retry, restore retry exhaustion, open-window maintenance, and final close. The existing taskbar lifecycle test also checks position changes. M.2 merged with passing CI; M.3 Windows CI and physical profiling remain pending.

Use equivalent idle-long workloads with no notes, closed notes without pending alarms, closed notes with pending alarms, and open notes. The primary expected improvement is reduced idle wakeups and allocation churn, not a guaranteed private-byte reduction. Verify alarm firing after sleep/resume, save retries, and desktop restoration manually. M.4 targets filesystem watcher consolidation and reuse.

## M.4: filesystem watcher consolidation

GitMetadataWatchers reconciles the desired exact filename subscriptions per nonrecursive directory rather than disposing all watchers on every repository refresh. HEAD, packed-refs and (in workspace mode) index share a watcher when they occupy the same metadata directory. The current loose ref uses its own directory watcher when necessary. Linked worktrees retain separate private and common metadata directories. A changed branch subscription replaces only the affected watcher, while unchanged subscriptions remain active. Rename notifications classify both old and new names, route refresh and index activity separately, and exclude unrelated files and lock files. Watching packed-refs even before it exists handles its subsequent creation.

A typical ordinary repository with an existing packed-refs file and loose ref previously used four metadata watchers in workspace mode; it now uses two. Pinned mode previously used three under the same conditions and now uses two. Without an existing packed-refs file the previous counts were smaller. These are subscription counts inferred from the implementation, not measured process handle or byte savings. No cross-repository sharing is introduced; the selected repository's independent watcher and explicit full workspace rescans are intentionally retained to preserve session and recovery behavior.

Metadata watcher errors invalidate the affected subscription and schedule a normal status refresh, allowing reconciliation to recreate it. No index promotion is fabricated for unknown overflow events. Missing or inaccessible directories are retried on subsequent reconciliation; this is not a guarantee of recovery without a further refresh if creation fails. Working-tree watchers now attach callbacks before enabling events and dispose partially initialized resources on failure. Their recursive scope, 64 KiB buffer, existing filters and debounce/recovery timings remain unchanged. Reducing buffers or sharing recursive watches across repositories is deferred unless measurements justify it.

Six tests cover watcher reuse and counts, changed refs, detached HEAD, clearing and disposal, linked worktrees, missing directory recovery, exact filename/rename routing, and live packed-refs/index/HEAD notifications. Existing workspace promotion and pinned behavior coverage remains in place. M.3 merged with passing CI; M.4 Windows CI and comparative profiling pending.

Profile a fixed workspace of known repository count and equivalent pinned mode before and after this change. Compare private bytes, handles, CPU and repeated activity; exercise branch checkout, index updates, packed refs, linked worktrees, workspace rescan and transition back to pinned mode. No measured MiB or process handle reductions claimed before matched captures. M.5 will address sticky-note rendering resources.

## M.5: sticky-note rendering resources

Sticky-note owner windows, passive bodies and appearance previews share a lazily initialized, bounded palette of frozen WPF brushes. The palette contains only the predefined note colors and fixed foreground/border colors, never arbitrary user content. Frozen brushes can be reused safely across dispatchers. The scrollbar style is loaded and sealed once per dispatcher through a weak-key cache; each editor and passive body keeps its own local implicit style entry. Other controls are not affected. The existing scrollbar templates, colors, transparency levels and click-through behavior remain unchanged.

Alarm pulses share a lazily initialized frozen animation definition but retain independent per-outline animation clocks and active glow effects. Dismissing an alarm removes its animation and local effect/brush references; inactive refreshes no longer repeatedly set collapsed visibility. Identical tooltip and automation label updates are skipped. The passive-body lifecycle, native affinity checks, alignment, focus, alarm deadlines and persistence are unchanged. The two-window click-through architecture is deliberately retained.

Three new STA tests cover frozen palette sharing across dispatchers, scrollbar style sharing and isolation, and independent alarm clocks/effect release. Existing scrollbar template, note appearance, editing, alarm and native affinity tests remain in the suite. M.4 merged with passing CI; M.5 CI and physical profiling remain pending.

Compare equivalent notes-10 and notes-closed workloads against the prior phase, including color changes, transparency transitions, collapse/expand, multiple simultaneous alarms, dismissal and virtual desktop switching. The bounded caches retain a small number of reusable resources after their first use; savings should be most apparent with multiple notes or repeated passive-window creation rather than an untouched tray-only session. No measured byte savings claimed. M.6 is evidence-driven runtime analysis; do not change GC or runtime settings without comparable measurements.

## M.6: runtime and GC evidence

BranchWatch targets .NET 10 with WPF and Windows Forms. The project declares no explicit GC mode, heap cap, latency policy or memory-conservation setting. Windows release validation publishes self-contained single-file x64 and arm64 executables. Workstation GC is the documented runtime default; the effective configuration can be overridden at launch, so source inspection alone is not proof of the actual running mode. No application code, runtime defaults or publish options are changed in this phase.

### Measure before tuning

Use the M.0 process sampler first, without instrumentation, on the merged M.5 release. Collect at least three independent process launches for every scenario. Then, in separate matched diagnostic runs, use Microsoft's optional dotnet-counters tool to inspect System.Runtime metrics. Install it explicitly only on a development machine if not already installed.

```powershell
dotnet tool install --global dotnet-counters
$bw = Get-Process -Name BranchWatch -ErrorAction Stop
if (@($bw).Count -ne 1) { throw 'Select exactly one BranchWatch instance.' }
dotnet-counters collect --process-id $bw.Id --refresh-interval 2 --format json --output runtime-idle.json --counters System.Runtime
```

Run for a fixed observation window, for example 60 seconds after warm-up, then stop collection with Ctrl+C. Choose a distinct output filename for each run. Record the diagnostic tool version, executable SHA-256, architecture, scenario, elapsed collection time and runtime experiment variant locally. Do not compare instrumented process-counter runs against uninstrumented ones. No heap dump or content-bearing diagnostic trace is needed for this initial assessment.

Inspect these .NET 9+ runtime measurements (counter presentation may differ by tool version):

| Metric | Interpretation |
| --- | --- |
| dotnet.gc.last_collection.memory.committed_size | GC committed virtual memory at the most recent collection, including capacity for future allocations. |
| dotnet.gc.last_collection.heap.size | Heap size by generation, including fragmentation. Sum generation values from the same observation, not across time. |
| dotnet.gc.last_collection.heap.fragmentation.size | Fragmentation by generation at the latest collection. |
| dotnet.gc.heap.total_allocated | Cumulative allocation counter; inspect rate or change, not total retained memory. |
| dotnet.gc.collections | Collection activity by generation. |
| dotnet.gc.pause.time | Accumulated GC pause time; compare changes over equal intervals. |
| dotnet.jit.compiled_methods, dotnet.assembly.count | Indicators of runtime loading/JIT activity, not native byte attribution. |

Latest-collection metrics can remain unchanged during idle periods. Do not force collection just to get a fresher metric. If GC committed memory is small relative to process private bytes, investigate native memory (WPF surfaces, native modules, stacks and allocators) instead; the difference is not an exact measurement of native live allocations. Use Windows VMMap or equivalent tooling only in separate diagnostic runs.

References: [GC runtime configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector), [runtime metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime), [dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters).

### Optional controlled experiment

Only if managed committed memory or fragmentation is material, compare default conservation level 0 against level 5 with the same self-contained executable. Use PowerShell 7.4+; exit BranchWatch normally via its tray before each launch, and confirm no remaining instance. Use disposable test data where needed and do not change machine-wide or user-wide environment variables. Review existing DOTNET/COMPlus runtime overrides locally and hold them constant (including any conflicting GC conservation override). Do not run both launches simultaneously.

```powershell
$exe = 'C:\path\to\BranchWatch.exe'
Start-Process -FilePath $exe -Environment @{ DOTNET_GCConserveMemory = '0' }
```

For the candidate, after normal exit, use:

```powershell
Start-Process -FilePath $exe -Environment @{ DOTNET_GCConserveMemory = '5' }
```

This startup-only child environment setting does not alter published configuration. The candidate may increase GC frequency or pauses. Alternate baseline and candidate runs, repeat the same warm-up and interactions, and use identical scenario/workload labels but distinct files (e.g. gc0-idle-1.json and gc5-idle-1.json). Keep release packaging and every other runtime setting unchanged. If existing overrides make the effective settings uncertain, invalidate the experiment rather than guessing.

### Compare independent runs

From PowerShell 7, use the new CompareRuns action with at least three profiles per group:

```powershell
$root = Join-Path $env:LOCALAPPDATA 'BranchWatch/memory-profiles'
$baseline = @(1..3 | ForEach-Object { Join-Path $root "gc0-idle-$_.json" })
$candidate = @(1..3 | ForEach-Object { Join-Path $root "gc5-idle-$_.json" })
& ./scripts/memory-profile.ps1 -Action CompareRuns -BaselinePaths $baseline -CandidatePaths $candidate -RequireSameExecutable
```

The comparison validates matching sampling conditions, distinct process launches and one executable hash per group. With RequireSameExecutable it requires the same binary across both groups. Without that switch it supports M.7 comparisons between two release builds. It reports medians of per-run medians (whole-run CPU utilization for CPU), minima, maxima, deltas and whether observed run ranges overlap. Range separation is descriptive, not a statistical significance test; repeat if results are noisy. The caller must still verify runtime settings, machine and workload equivalence; the executable hash cannot establish environment overrides or companion DLL identity for non-single-file builds.

### Decision gate

Retain defaults unless private-byte reductions are meaningful, repeatable beyond baseline run variation and observed in relevant workloads. A lower working set alone is insufficient. Require equivalent CPU, UI responsiveness, alarm delivery, native desktop affinity, watcher behavior, and browser/restore qualification. Inspect collection pauses in separately instrumented runs. Reject a candidate if it introduces meaningful regressions or the evidence is ambiguous. Do not add periodic GC.Collect, working-set trimming, hard heap limits, server GC, aggressive latency policies, trimming/AOT or rendering-mode overrides as speculative changes. Any justified production runtime change requires a separate review and PR.

The existing CI memory-profile self-test now also covers multi-run statistics, overlapping ranges, duplicate launches, insufficient runs, mixed hashes and identical-binary enforcement. M.5 merged with passing Windows CI; M.6 Windows CI and physical data remain pending. No runtime tuning adopted without evidence. M.7 will compare M.0-era and final release binaries and exercise the functional qualification matrix.
