namespace FamilyLedger.Api.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            _logger.LogError(
                "Unhandled API exception of type {ExceptionType} for correlation ID {CorrelationId}",
                exception.GetType().FullName,
                context.TraceIdentifier);

            context.Response.Clear();
            context.Response.Headers[CorrelationIdMiddleware.HeaderName] = context.TraceIdentifier;
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsJsonAsync(new
            {
                code = "unexpected_error",
                message = "An unexpected error occurred. Please try again later.",
                correlationId = context.TraceIdentifier
            });
        }
    }
}
