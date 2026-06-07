using System.Diagnostics.Metrics;

namespace ProjectTrackerTemplate.Projects.Infrastructure;

// Projects meter — registered in Program.cs via
//   builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddMeter(ProjectsMetrics.MeterName))
// so the Aspire dashboard surfaces it in the Metrics tab alongside the stock
// AspNetCore / HttpClient / Runtime instrumentation.
//
// The ResourceLoads counter increments INSIDE InMemoryProjectRepository.FindByIdAsync
// (the ACL boundary). That placement matters: it counts every load that crosses the
// boundary, including any handler that bypasses the v4 accessor and re-loads via
// the repository directly. If the counter ever shows N=2 per single request, the
// v4 accessor pattern has regressed.
public static class ProjectsMetrics
{
    public const string MeterName = "ProjectTracker.Projects";

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ResourceLoads = Meter.CreateCounter<long>(
        name: "projects.resource_loads",
        unit: "{load}",
        description: "Number of times a Project aggregate was loaded from the repository (ACL boundary).");
}
