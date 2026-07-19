using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PitCrew.Dashboard.Kernel.ExceptionHandling;

internal sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> _logger,
    IHostEnvironment _environment) : IExceptionHandler
{
  [LoggerMessage(
      Level = LogLevel.Error,
      Message = "Unhandled exception processing request {Method} {Path}")]
  private partial void LogUnhandledException(
      string method,
      string path,
      Exception exception);

  public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext,
      Exception exception,
      CancellationToken cancellationToken)
  {
    LogUnhandledException(
        httpContext.Request.Method,
        httpContext.Request.Path,
        exception);

    httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

    var problemDetails = new ProblemDetails
    {
      Status = StatusCodes.Status500InternalServerError,
      Title = "An unexpected error occurred",
      Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };

    if (_environment.IsDevelopment())
    {
      problemDetails.Detail = exception.ToString();
    }

    await httpContext.Response.WriteAsJsonAsync(
        problemDetails,
        cancellationToken);

    return true;
  }
}
