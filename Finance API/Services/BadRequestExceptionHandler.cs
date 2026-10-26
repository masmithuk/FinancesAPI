using Microsoft.AspNetCore.Diagnostics;

namespace Finance_API;

/// <summary>Maps malformed-request exceptions to their 4xx status instead of a generic 500.</summary>
internal sealed class BadRequestExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new { title = "The request could not be processed.", status = badRequest.StatusCode },
            cancellationToken);
        return true;
    }
}
