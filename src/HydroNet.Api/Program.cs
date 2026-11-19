using HydroNet.Core;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<DashboardAggregator>();
builder.Services.AddSingleton<JitAccessService>();
builder.Services.AddSingleton<AlarmFatigueReducer>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { service = "HydroNet.Control", status = "ok" }));

app.MapGet("/api/dashboard", async (DashboardAggregator aggregator, CancellationToken cancellationToken) =>
{
    var snapshot = await aggregator.AggregateAsync(
        _ => Task.FromResult<IReadOnlyList<PumpStation>>(new[] { new PumpStation("PS-01", "Северная насосная", "Север") }),
        _ => Task.FromResult<IReadOnlyList<PressureAlert>>(new[] { new PressureAlert("A-1", "ROOT-1", "PS-01", AlertPriority.P2, "Падение давления", DateTimeOffset.UtcNow) }),
        _ => Task.FromResult<IReadOnlyList<RepairCrewOrder>>(Array.Empty<RepairCrewOrder>()),
        cancellationToken);
    return Results.Ok(snapshot);
});

app.MapPost("/api/jit/request", (JitAccessService jit, JitRequestDto dto) => Results.Ok(jit.Request(dto.UserId, dto.Reason, DateTimeOffset.UtcNow)));
app.MapPost("/api/jit/{id:guid}/approve", (Guid id, JitAccessService jit, ApproveJitDto dto) => Results.Ok(jit.Approve(id, dto.Approver, DateTimeOffset.UtcNow)));
app.MapGet("/api/jit/{id:guid}/active", (Guid id, JitAccessService jit) => Results.Ok(new { active = jit.IsActive(id, DateTimeOffset.UtcNow) }));
app.MapHub<HydroNetHub>("/hubs/telemetry");
app.Run();

public sealed record JitRequestDto(string UserId, string Reason);
public sealed record ApproveJitDto(string Approver);
public sealed class HydroNetHub : Hub
{
    public Task SubscribeDistrict(string district) => Groups.AddToGroupAsync(Context.ConnectionId, district);
}

public partial class Program;
