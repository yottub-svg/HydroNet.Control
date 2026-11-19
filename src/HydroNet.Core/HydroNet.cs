using System.Collections.Concurrent;

namespace HydroNet.Core;

public enum AlertPriority { P1 = 1, P2 = 2, P3 = 3 }

public sealed record PumpStation(string Id, string Name, string District);
public sealed record PressureReading(string StationId, double Bar, DateTimeOffset At);
public sealed record PressureAlert(string Id, string RootCauseId, string StationId, AlertPriority Priority, string Message, DateTimeOffset At);
public sealed record RepairCrewOrder(string Id, string AlertId, string District, string Status, DateTimeOffset CreatedAt);

public sealed record ValveEdge(string From, string To, string ValveId, double UpstreamBar, double DownstreamBar)
{
    public double Gradient => Math.Abs(UpstreamBar - DownstreamBar);
}

public sealed record LeakLocalizationResult(string SuspectValveId, IReadOnlyList<string> ValvesToClose, double GradientBar, TimeSpan AnalysisTime);

public sealed class PressureNetwork
{
    private readonly Dictionary<string, ValveEdge> edges = new(StringComparer.OrdinalIgnoreCase);

    public void AddEdge(ValveEdge edge) => edges[edge.ValveId] = edge;

    public LeakLocalizationResult Localize(IReadOnlyCollection<string> affectedStations)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var candidates = edges.Values
            .Where(e => affectedStations.Contains(e.From, StringComparer.OrdinalIgnoreCase) || affectedStations.Contains(e.To, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Gradient)
            .ThenBy(e => e.ValveId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var best = candidates.FirstOrDefault() ?? throw new InvalidOperationException("No valve edges match the affected stations.");
        var elapsed = TimeSpan.FromSeconds((double)(System.Diagnostics.Stopwatch.GetTimestamp() - started) / System.Diagnostics.Stopwatch.Frequency);
        return new LeakLocalizationResult(best.ValveId, candidates.Take(Math.Min(3, candidates.Length)).Select(e => e.ValveId).ToArray(), best.Gradient, elapsed);
    }
}

public sealed record AlarmCluster(string RootCauseId, AlertPriority Priority, IReadOnlyList<PressureAlert> Alerts)
{
    public string FocusMessage => Alerts.OrderBy(a => a.At).First().Message;
}

public sealed class AlarmFatigueReducer
{
    public IReadOnlyList<AlarmCluster> Group(IEnumerable<PressureAlert> alerts) => alerts
        .GroupBy(a => a.RootCauseId, StringComparer.OrdinalIgnoreCase)
        .Select(g => new AlarmCluster(g.Key, g.Min(a => a.Priority), g.OrderBy(a => a.At).ToArray()))
        .OrderBy(c => c.Priority)
        .ThenBy(c => c.RootCauseId, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed record DashboardSnapshot(IReadOnlyList<PumpStation> Stations, IReadOnlyList<PressureAlert> Alerts, IReadOnlyList<RepairCrewOrder> Orders, bool AlertsDegraded);

public sealed class DashboardAggregator
{
    public async Task<DashboardSnapshot> AggregateAsync(
        Func<CancellationToken, Task<IReadOnlyList<PumpStation>>> stations,
        Func<CancellationToken, Task<IReadOnlyList<PressureAlert>>> alerts,
        Func<CancellationToken, Task<IReadOnlyList<RepairCrewOrder>>> orders,
        CancellationToken cancellationToken)
    {
        var stationTask = stations(cancellationToken);
        var alertTask = alerts(cancellationToken);
        var orderTask = orders(cancellationToken);
        try
        {
            await Task.WhenAll(stationTask, alertTask, orderTask).ConfigureAwait(false);
            return new DashboardSnapshot(await stationTask, await alertTask, await orderTask, false);
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            var stationResult = stationTask.Status == TaskStatus.RanToCompletion ? await stationTask : Array.Empty<PumpStation>();
            var alertResult = alertTask.Status == TaskStatus.RanToCompletion ? await alertTask : Array.Empty<PressureAlert>();
            var orderResult = orderTask.Status == TaskStatus.RanToCompletion ? await orderTask : Array.Empty<RepairCrewOrder>();
            return new DashboardSnapshot(stationResult, alertResult, orderResult, alertTask.Status != TaskStatus.RanToCompletion);
        }
    }
}

public sealed record JitRequest(Guid Id, string UserId, string Reason, string Status, DateTimeOffset RequestedAt, DateTimeOffset? ExpiresAt);
public sealed record AuditEntry(DateTimeOffset At, string Actor, string Action, Guid RequestId);

public sealed class JitAccessService
{
    private readonly ConcurrentDictionary<Guid, JitRequest> requests = new();
    private readonly ConcurrentQueue<AuditEntry> audit = new();
    public IReadOnlyCollection<AuditEntry> Audit => audit.ToArray();

    public JitRequest Request(string userId, string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("User and reason are required.");
        var request = new JitRequest(Guid.NewGuid(), userId, reason, "Pending", now, null);
        requests[request.Id] = request;
        audit.Enqueue(new AuditEntry(now, userId, "JIT_REQUESTED", request.Id));
        return request;
    }

    public JitRequest Approve(Guid requestId, string approver, DateTimeOffset now)
    {
        if (!requests.TryGetValue(requestId, out var request)) throw new KeyNotFoundException("JIT request not found.");
        if (string.Equals(request.UserId, approver, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Self-approval is forbidden.");
        var approved = request with { Status = "Approved", ExpiresAt = now.AddMinutes(15) };
        requests[requestId] = approved;
        audit.Enqueue(new AuditEntry(now, approver, "JIT_APPROVED", requestId));
        return approved;
    }

    public bool IsActive(Guid requestId, DateTimeOffset now) => requests.TryGetValue(requestId, out var request) && request.Status == "Approved" && request.ExpiresAt > now;
}

public sealed record ScreenState(string ScreenId, string MonitorId, string SelectedStationId);

public sealed class ScreenStateStore
{
    private readonly ConcurrentDictionary<string, ScreenState> states = new(StringComparer.OrdinalIgnoreCase);
    public void Set(ScreenState state) => states[state.ScreenId] = state;
    public IReadOnlyCollection<ScreenState> Snapshot() => states.Values.OrderBy(x => x.ScreenId, StringComparer.OrdinalIgnoreCase).ToArray();
}

public enum ConnectionStatus { Connected, Reconnecting, Degraded }

public sealed class ReconnectCoordinator
{
    private readonly ConcurrentQueue<PressureAlert> missedAlerts = new();
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Connected;
    public void Disconnected() => Status = ConnectionStatus.Reconnecting;
    public void Buffer(PressureAlert alert) => missedAlerts.Enqueue(alert);
    public IReadOnlyList<PressureAlert> Reconnected()
    {
        Status = ConnectionStatus.Connected;
        var result = new List<PressureAlert>();
        while (missedAlerts.TryDequeue(out var alert)) result.Add(alert);
        return result;
    }
}

public sealed record KeepalivedNode(string Name, string Address, bool Primary);
public sealed record NetworkPolicy(string SourceZone, string DestinationZone, int Port, string Protocol, string Action);
