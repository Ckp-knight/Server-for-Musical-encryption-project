using System.Diagnostics;
using MusicalEncryptionServer.Diagnostics;

namespace MusicalEncryptionServer.Middleware;

/// <summary>
/// Prints one line to the console for every HTTP request, so you can watch what
/// the server is doing without opening the debugger:
///   --> POST /api/encryption            user=user-123
///   &lt;-- POST /api/encryption            201  in 4 ms
/// Also records the same information in <see cref="IRequestLogStore"/>, which is
/// what feeds the live feed on the /dev control panel.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;
    private readonly IRequestLogStore _requestLog;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, IRequestLogStore requestLog)
    {
        _next = next;
        _logger = logger;
        _requestLog = requestLog;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.ToString();
        var user = context.Request.Headers["X-User-Id"].FirstOrDefault();

        _logger.LogInformation("--> {Method} {Path}   user={User}", method, path, user ?? "(none)");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation("<-- {Method} {Path}   {StatusCode} in {Elapsed} ms",
                method, path, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);

            _requestLog.Add(new RequestLogEntry(
                DateTimeOffset.Now, method, path, user, context.Response.StatusCode, stopwatch.ElapsedMilliseconds));
        }
    }
}
