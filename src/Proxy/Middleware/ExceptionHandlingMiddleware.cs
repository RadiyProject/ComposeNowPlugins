using ComposeNowPlugins.Exceptions;

namespace ComposeNowPlugins.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger
)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger = logger;

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
            context.Response.StatusCode = GetStatusCode(exception);

            await context.Response.WriteAsync(
                exception.Message,
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
}
