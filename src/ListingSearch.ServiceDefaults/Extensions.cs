using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// The shared kernel: telemetry, health, service discovery and resilience.
///
/// P2 — this is a shared <em>kernel</em>, not a shared <em>domain</em>. It contains no
/// listing model, no filter type, no ranking constant and no scenario vocabulary,
/// kept well under the ~800-line ceiling the estate applies to a kernel project. The
/// search vocabulary — index calls, ranking events, turn outcomes — lives in the
/// service that owns it, not here. A future second service (e.g. a standalone
/// ingestion worker) would share the plumbing below and none of the domain.
/// </summary>
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    /// <summary>
    /// Every service calls this (P2a). A service that opts out of the kernel opts out
    /// of being operable.
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Resilience on by default, on every outbound client, with explicit
            // timeouts. Opting in per-client is how one client ends up without it.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// OTLP first (P15). The service emits traces, metrics and logs whether or not
    /// anything is listening, and an exporter is attached only when an endpoint is
    /// configured.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        // Probe noise would otherwise dominate every trace view.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// Optional dependency, degraded rather than required (P8). No endpoint
    /// configured means no exporter — not a startup failure. The spans are still
    /// produced, which is what lets the eval harness read them in-process with
    /// nothing configured at all.
    /// </summary>
    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// <c>/health</c> is readiness — every check must pass. <c>/alive</c> is liveness —
    /// only <c>live</c>-tagged checks.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath);

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });

        return app;
    }
}
