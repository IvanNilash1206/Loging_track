using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LogSystem.Dashboard.Filters;

/// <summary>
/// Global exception filter that catches Firestore/gRPC errors and returns
/// graceful HTTP responses instead of 500s.
/// </summary>
public class FirestoreExceptionFilter : IExceptionFilter
{
    private readonly ILogger<FirestoreExceptionFilter> _logger;

    public FirestoreExceptionFilter(ILogger<FirestoreExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.Exception is RpcException rpcEx)
        {
            _logger.LogWarning("Firestore gRPC error: {Status} — {Detail}", rpcEx.StatusCode, rpcEx.Status.Detail);

            var (httpStatus, message) = rpcEx.StatusCode switch
            {
                StatusCode.ResourceExhausted => (429, "Firestore quota exceeded. Please wait and try again."),
                StatusCode.Unauthenticated => (503, "Firestore authentication failed. Check service account."),
                StatusCode.Unavailable => (503, "Firestore temporarily unavailable. Retrying..."),
                StatusCode.PermissionDenied => (403, "Firestore permission denied. Check security rules."),
                StatusCode.NotFound => (404, "Firestore resource not found."),
                _ => (502, $"Firestore error: {rpcEx.Status.Detail}")
            };

            context.Result = new ObjectResult(new { error = message, firestoreStatus = rpcEx.StatusCode.ToString() })
            {
                StatusCode = httpStatus
            };
            context.ExceptionHandled = true;
        }
    }
}
