namespace GeoSurvey.Api.Configuration;

/// <summary>Keep provider/credential diagnostics out of public responses and request exception logs.</summary>
public sealed class SafeProblemMiddleware(RequestDelegate next, ILogger<SafeProblemMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Request could not complete. Category {Category}; trace {TraceId}.", exception.GetType().Name, context.TraceIdentifier);
            if (context.Response.HasStarted)
            {
                context.Abort();
                return;
            }
            context.Response.Clear();
            var status = exception is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError;
            await Results.Problem(statusCode: status,
                title: status < 500 ? "The request is not valid." : "The request could not be completed.",
                extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        }
    }
}
