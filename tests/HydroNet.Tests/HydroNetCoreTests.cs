using HydroNet.Core;

namespace HydroNet.Tests;

public class HydroNetCoreTests
{
    [Fact]
    public void Localizer_selects_highest_pressure_gradient()
    {
        var network = new PressureNetwork();
        network.AddEdge(new ValveEdge("A", "B", "V-01", 6.0, 5.8));
        network.AddEdge(new ValveEdge("B", "C", "V-02", 6.0, 3.1));

        var result = network.Localize(new[] { "A", "B", "C" });

        Assert.Equal("V-02", result.SuspectValveId);
        Assert.True(result.GradientBar > 2.8);
        Assert.True(result.AnalysisTime < TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void Alarm_reducer_groups_cascade_and_keeps_highest_priority()
    {
        var now = DateTimeOffset.UtcNow;
        var reducer = new AlarmFatigueReducer();
        var clusters = reducer.Group(new[]
        {
            new PressureAlert("1", "ROOT", "A", AlertPriority.P3, "minor", now),
            new PressureAlert("2", "ROOT", "B", AlertPriority.P1, "rupture", now.AddSeconds(1))
        });

        var cluster = Assert.Single(clusters);
        Assert.Equal(AlertPriority.P1, cluster.Priority);
        Assert.Equal(2, cluster.Alerts.Count);
    }

    [Fact]
    public async Task Bff_aggregator_returns_partial_snapshot_when_secondary_source_fails()
    {
        var aggregator = new DashboardAggregator();
        var snapshot = await aggregator.AggregateAsync(
            _ => Task.FromResult<IReadOnlyList<PumpStation>>(new[] { new PumpStation("P1", "North", "N") }),
            _ => Task.FromException<IReadOnlyList<PressureAlert>>(new InvalidOperationException("secondary down")),
            _ => Task.FromResult<IReadOnlyList<RepairCrewOrder>>(Array.Empty<RepairCrewOrder>()),
            CancellationToken.None);

        Assert.Single(snapshot.Stations);
        Assert.True(snapshot.AlertsDegraded);
    }

    [Fact]
    public void Jit_access_is_not_active_before_approval_and_expires_after_15_minutes()
    {
        var service = new JitAccessService();
        var now = DateTimeOffset.Parse("2026-11-19T10:00:00+00:00");
        var request = service.Request("dispatcher-1", "Emergency valve operation", now);
        Assert.False(service.IsActive(request.Id, now));

        service.Approve(request.Id, "senior-1", now);
        Assert.True(service.IsActive(request.Id, now.AddMinutes(14)));
        Assert.False(service.IsActive(request.Id, now.AddMinutes(15).AddSeconds(1)));
        Assert.Equal(2, service.Audit.Count);
    }

    [Fact]
    public void Jit_access_forbids_self_approval()
    {
        var service = new JitAccessService();
        var request = service.Request("dispatcher-1", "Emergency", DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => service.Approve(request.Id, "dispatcher-1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Screen_store_keeps_state_for_both_monitors()
    {
        var store = new ScreenStateStore();
        store.Set(new ScreenState("map", "MON-1", "PS-01"));
        store.Set(new ScreenState("alarms", "MON-2", "PS-01"));

        Assert.Equal(2, store.Snapshot().Count);
        Assert.Contains(store.Snapshot(), x => x.MonitorId == "MON-2");
    }

    [Fact]
    public void Reconnect_flushes_all_buffered_alerts_without_loss()
    {
        var coordinator = new ReconnectCoordinator();
        coordinator.Disconnected();
        coordinator.Buffer(new PressureAlert("A1", "R1", "PS-01", AlertPriority.P1, "burst", DateTimeOffset.UtcNow));
        coordinator.Buffer(new PressureAlert("A2", "R1", "PS-01", AlertPriority.P2, "drop", DateTimeOffset.UtcNow));

        var replay = coordinator.Reconnected();

        Assert.Equal(ConnectionStatus.Connected, coordinator.Status);
        Assert.Equal(2, replay.Count);
    }

    [Fact]
    public void Network_policy_contains_only_explicit_allowed_protocols()
    {
        var policies = new[]
        {
            new NetworkPolicy("SCADA", "DMZ", 443, "TCP", "ALLOW"),
            new NetworkPolicy("SCADA", "OFFICE", 0, "ANY", "DENY")
        };

        Assert.Contains(policies, p => p.Port == 443 && p.Action == "ALLOW");
        Assert.Contains(policies, p => p.DestinationZone == "OFFICE" && p.Action == "DENY");
    }
}
