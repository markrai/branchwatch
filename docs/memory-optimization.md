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
| M.0 | Repeatable profiling, baseline and comparison | Harness implemented; interactive baseline pending |
| M.1 | Lazy overlay window creation and resource release | Planned |
| M.2 | Lazy settings pages and snapshot loading | Planned |
| M.3 | Demand-driven timers and background activity | Planned |
| M.4 | Filesystem watcher consolidation and reuse | Planned |
| M.5 | Sticky-note rendering and shared resources | Planned |
| M.6 | Controlled runtime/GC experiments only if evidence warrants | Planned |
| M.7 | Final repeat profiling and functional regression qualification | Planned |

Start with private-byte and resource-count improvements in equivalent workloads; set numerical targets only after actual controlled baseline data. Retain the R.8 maintainability and physical qualification boundaries. No runtime flags, application behavior, storage format or dependencies change in M.0.
