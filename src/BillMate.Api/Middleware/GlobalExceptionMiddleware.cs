using System.Diagnostics;
using System.Text.Json;
using BillMate.Application.Common.Exceptions;
using BillMate.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var (status, code, message, errors) = ex switch
        {
            ValidationAppException valEx => (valEx.HttpStatus, valEx.ErrorCode, valEx.Message, valEx.Errors),
            AppException appEx => (appEx.HttpStatus, appEx.ErrorCode, appEx.Message, null),
            InsufficientStockException stockEx => (StatusCodes.Status422UnprocessableEntity, stockEx.ErrorCode, stockEx.Message, null),
            DomainException domEx => (StatusCodes.Status400BadRequest, domEx.ErrorCode, domEx.Message, null),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "SYNC_001", "The record was modified by another device. Please refresh and retry.", null),
            _ => (StatusCodes.Status500InternalServerError, "SYS_500", "An unexpected server error occurred.", null)
        };

        if (status >= 500)
            _logger.LogError(ex, "Unhandled exception TraceId={TraceId}", traceId);
        else
            _logger.LogWarning("Handled business error {Code} ({Status}) TraceId={TraceId}: {Message}", code, status, traceId, message);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            code,
            message,
            traceId,
            errors
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
