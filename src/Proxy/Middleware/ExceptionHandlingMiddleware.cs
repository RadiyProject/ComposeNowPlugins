using ComposeNowPlugins.Application.Exceptions;

namespace ComposeNowPlugins.Proxy.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment
)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger = logger;
    private readonly IHostEnvironment _environment = environment;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unhandled request exception. Path={Path}",
                context.Request.Path
            );

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.ContentType = "text/plain; charset=utf-8";
            int statusCode = GetStatusCode(exception);
            context.Response.StatusCode = statusCode;

            await context.Response.WriteAsync(
                GetResponseMessage(exception, statusCode),
                context.RequestAborted
            );
        }
    }

    private static int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            EntityNotFoundException => StatusCodes.Status404NotFound,
            EntityAlreadyExistsException => StatusCodes.Status409Conflict,
            RepositoryException => StatusCodes.Status500InternalServerError,
            AppException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private string GetResponseMessage(Exception exception, int statusCode)
    {
        return statusCode >= StatusCodes.Status500InternalServerError && !_environment.IsDevelopment()
            ? "An internal server error occurred."
            : exception.Message;
    }
}
