using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FamilyLedger.Api.Health;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            correlationId = context.TraceIdentifier,
            durationMilliseconds = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks = report.Entries
                .OrderBy(entry => entry.Key)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString().ToLowerInvariant(),
                    durationMilliseconds = Math.Round(entry.Value.Duration.TotalMilliseconds, 2)
                })
        });
    }
}
