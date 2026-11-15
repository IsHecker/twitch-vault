using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace TwitchVault.Api.Features.Testing.Endpoints;

// ── USAGE ──────────────────────────────────────────────────────────────────────
// GET /diagnostics/memory                   current snapshot
// GET /diagnostics/memory?forceGc=true      also reports memory after a full blocking GC (shows what is really retained).
//                                           Blocks the app for a moment: use it every ~10 min, not every poll.
// GET /diagnostics/memory?modules=true      lists loaded native modules (Media Foundation DLLs etc.)
//
// appsettings.json (all optional, these are the defaults):
//   "Diagnostics": { "MemoryLimitMb": 256, "WarnPercent": 65, "CriticalPercent": 85 }
//
// Optional, to see WHAT the app was doing when memory moved (inside CompileAsync):
//   using var _ = DiagnosticsActivity.Begin("storyboard-compile", stream.Id.ToString());
//   Nest more scopes for phases, e.g. Begin("capture-frames"), Begin("write-outputs").
//
// Key fields for an analyzer:
//   current.process.privateMb       committed private memory
//   current.process.workingSetMb    resident memory (includes mapped DLL images); some hosts limit on this
//   current.nativeEstimateMb        privateMb - GC.GetTotalMemory = decoder, ImageSharp pool, DLLs, stacks
//   afterForcedGc.managed.gcTotalMb managed memory that is truly retained (a growing number = managed leak)
//   limit.usedMb / usedPercent      max(privateMb, workingSetMb) against the limit; status is ok | warn | critical
//   activeActivities                what was running at this moment

public sealed class MemoryDiagnosticsEndpoint : IEndpoint
{
    private static readonly string[] GenerationNames = ["gen0", "gen1", "gen2", "loh", "poh"];
    private static readonly string[] InterestingModulePrefixes = ["mf", "msmpeg", "evr", "d3d11", "dxgi", "ole32"];

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/diagnostics/memory", Handle);
    }

    private IResult Handle(
        HttpContext http,
        IConfiguration config,
        bool? forceGc,
        bool? modules)
    {
        var limitMb = config.GetValue("Diagnostics:MemoryLimitMb", 256);
        var warnPercent = config.GetValue("Diagnostics:WarnPercent", 65);
        var criticalPercent = config.GetValue("Diagnostics:CriticalPercent", 85);

        using var process = Process.GetCurrentProcess();

        var current = Capture(process);

        MemorySnapshot? afterGc = null;
        if (forceGc == true)
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
            afterGc = Capture(process);
        }

        // Hosts differ in what they count against the limit (private bytes vs working set),
        // so judge against the worse of the two.
        var usedMb = Math.Max(current.Process.PrivateMb, current.Process.WorkingSetMb);
        var usedPercent = Math.Round(usedMb / limitMb * 100, 1);
        var status = usedPercent >= criticalPercent ? "critical"
            : usedPercent >= warnPercent ? "warn"
            : "ok";

        double? uptimeSeconds = null;
        try { uptimeSeconds = Math.Round((DateTime.Now - process.StartTime).TotalSeconds, 0); }
        catch { /* restricted on some hosts */ }

        ThreadPool.GetAvailableThreads(out var availableWorkers, out _);

        var report = new MemoryReport(
            TakenAtUtc: DateTimeOffset.UtcNow,
            UptimeSeconds: uptimeSeconds,
            Status: status,
            Limit: new LimitInfo(limitMb, usedMb, usedPercent, Math.Round(limitMb - usedMb, 1), warnPercent, criticalPercent),
            Current: current,
            AfterForcedGc: afterGc,
            ActiveActivities: DiagnosticsActivity.Snapshot(),
            ThreadPool: new ThreadPoolInfo(
                ThreadPool.ThreadCount,
                availableWorkers,
                ThreadPool.PendingWorkItemCount,
                ThreadPool.CompletedWorkItemCount),
            Runtime: new RuntimeDetails(
                Environment.Version.ToString(),
                RuntimeInformation.OSDescription,
                Environment.Is64BitProcess,
                Environment.ProcessorCount,
                GCSettings.IsServerGC,
                GCSettings.LatencyMode.ToString(),
                AppContext.GetData("GCHeapHardLimit")?.ToString()),
            Modules: modules == true ? ReadModules(process) : null);

        http.Response.Headers.CacheControl = "no-store";
        return Results.Json(report);
    }

    private static MemorySnapshot Capture(Process process)
    {
        process.Refresh();
        var gcInfo = GC.GetGCMemoryInfo();

        var processMemory = new ProcessMemory(
            PrivateMb: Mb(process.PrivateMemorySize64),
            WorkingSetMb: Mb(process.WorkingSet64),
            PeakWorkingSetMb: Mb(process.PeakWorkingSet64),
            VirtualMb: Mb(process.VirtualMemorySize64),
            PagedMb: Mb(process.PagedMemorySize64),
            PeakPagedMb: Mb(process.PeakPagedMemorySize64),
            Handles: process.HandleCount,
            Threads: process.Threads.Count);

        var managed = new ManagedMemory(
            GcTotalMb: Mb(GC.GetTotalMemory(forceFullCollection: false)),
            HeapSizeMb: Mb(gcInfo.HeapSizeBytes),
            CommittedMb: Mb(gcInfo.TotalCommittedBytes),
            FragmentedMb: Mb(gcInfo.FragmentedBytes),
            TotalAllocatedMb: Mb(GC.GetTotalAllocatedBytes(precise: false)),
            GcAvailableMb: Mb(gcInfo.TotalAvailableMemoryBytes),
            MemoryLoadMb: Mb(gcInfo.MemoryLoadBytes),
            HighMemoryLoadThresholdMb: Mb(gcInfo.HighMemoryLoadThresholdBytes),
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            TotalPauseMs: Math.Round(GC.GetTotalPauseDuration().TotalMilliseconds, 1),
            LastGcGeneration: gcInfo.Generation,
            LastGcPausePercent: Math.Round(gcInfo.PauseTimePercentage, 2),
            LastGcGenerations: Generations(gcInfo));

        // Private bytes the GC doesn't account for: Media Foundation, ImageSharp pool, DLLs, thread stacks, JIT.
        // Uses GC.GetTotalMemory (always populated) instead of the last-GC committed size, which reads 0
        // until the first collection has run. Slightly overestimates native by the GC's committed-but-free space.
        var nativeEstimate = Math.Max(0, Math.Round(processMemory.PrivateMb - managed.GcTotalMb, 1));

        return new MemorySnapshot(processMemory, managed, nativeEstimate);
    }

    // Values come from the LAST GC, so they are all zero until the first collection has run.
    private static List<GenerationReport> Generations(in GCMemoryInfo info)
    {
        var list = new List<GenerationReport>();
        var generations = info.GenerationInfo;

        for (var i = 0; i < generations.Length && i < GenerationNames.Length; i++)
        {
            list.Add(new GenerationReport(
                GenerationNames[i],
                Mb(generations[i].SizeAfterBytes),
                Mb(generations[i].FragmentationAfterBytes)));
        }

        return list;
    }

    private static object? ReadModules(Process process)
    {
        try
        {
            var all = process.Modules.Cast<ProcessModule>().ToList();
            var interesting = all
                .Where(m => InterestingModulePrefixes.Any(p =>
                    m.ModuleName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .Select(m => new { name = m.ModuleName, imageMb = Mb(m.ModuleMemorySize) })
                .OrderByDescending(m => m.imageMb)
                .ToList();

            return new
            {
                count = all.Count,
                totalImageMb = Mb(all.Sum(m => (long)m.ModuleMemorySize)),
                interesting
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }

    private static double Mb(long bytes) => Math.Round(bytes / 1_048_576.0, 1);
}

/// <summary>Tags "what is running right now" so memory samples can be correlated with work.</summary>
public static class DiagnosticsActivity
{
    private static readonly ConcurrentDictionary<long, Entry> Active = new();
    private static long _nextId;

    public static IDisposable Begin(string kind, string? detail = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        Active[id] = new Entry(kind, detail, DateTimeOffset.UtcNow);
        return new Scope(id);
    }

    public static IReadOnlyList<ActivityInfo> Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return Active
            .OrderBy(kv => kv.Key)
            .Select(kv => new ActivityInfo(
                kv.Value.Kind,
                kv.Value.Detail,
                Math.Round((now - kv.Value.StartedAtUtc).TotalSeconds, 1)))
            .ToList();
    }

    private sealed record Entry(string Kind, string? Detail, DateTimeOffset StartedAtUtc);

    private sealed class Scope(long id) : IDisposable
    {
        public void Dispose() => Active.TryRemove(id, out _);
    }
}

// ── Response models ────────────────────────────────────────────────────────────
public sealed record MemoryReport(
    DateTimeOffset TakenAtUtc,
    double? UptimeSeconds,
    string Status,
    LimitInfo Limit,
    MemorySnapshot Current,
    MemorySnapshot? AfterForcedGc,
    IReadOnlyList<ActivityInfo> ActiveActivities,
    ThreadPoolInfo ThreadPool,
    RuntimeDetails Runtime,
    object? Modules);

public sealed record LimitInfo(int LimitMb, double UsedMb, double UsedPercent, double HeadroomMb, int WarnPercent, int CriticalPercent);

public sealed record MemorySnapshot(ProcessMemory Process, ManagedMemory Managed, double NativeEstimateMb);

public sealed record ProcessMemory(
    double PrivateMb,
    double WorkingSetMb,
    double PeakWorkingSetMb,
    double VirtualMb,
    double PagedMb,
    double PeakPagedMb,
    int Handles,
    int Threads);

public sealed record ManagedMemory(
    double GcTotalMb,
    double HeapSizeMb,
    double CommittedMb,
    double FragmentedMb,
    double TotalAllocatedMb,
    double GcAvailableMb,
    double MemoryLoadMb,
    double HighMemoryLoadThresholdMb,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double TotalPauseMs,
    int LastGcGeneration,
    double LastGcPausePercent,
    IReadOnlyList<GenerationReport> LastGcGenerations);

public sealed record GenerationReport(string Name, double SizeMb, double FragmentationMb);

public sealed record ActivityInfo(string Kind, string? Detail, double RunningSeconds);

public sealed record ThreadPoolInfo(int Threads, int AvailableWorkers, long PendingWorkItems, long CompletedWorkItems);

public sealed record RuntimeDetails(
    string Version,
    string Os,
    bool Is64Bit,
    int ProcessorCount,
    bool ServerGc,
    string GcLatencyMode,
    string? GcHeapHardLimit);