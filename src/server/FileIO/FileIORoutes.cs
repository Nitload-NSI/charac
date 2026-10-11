using Microsoft.AspNetCore.Mvc;
using Charac.Server.Authentication;
using Charac.Server.Connections;
using Charac.Server.Hosting;

namespace Charac.Server.FileIO;

internal static class FileIORoutes
{
    public static void Map(WebApplication app) => app.MapPost("/files/upload", UploadAsync)
        .RequireAuthorization()
        .WithMetadata(new RequestSizeLimitAttribute(512L * 1024 * 1024));

    private static async Task<IResult> UploadAsync(
        HttpContext context,
        [FromQuery(Name = "target")] Guid? targetId,
        [FromQuery(Name = "name")] string? fileName,
        SshAccessResolver access,
        SshFileUploader uploader,
        ActiveClientDevices devices,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("FileIO");
        if (targetId is null || targetId == Guid.Empty)
            return Results.BadRequest(new { error = "target_required" });
        if (string.IsNullOrWhiteSpace(fileName))
            return Results.BadRequest(new { error = "file_name_required" });
        if (context.Request.ContentLength is > 512L * 1024 * 1024)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!ClientSessionRoutes.TryIdentity(context.User, out var owner))
            return Results.Unauthorized();

        var deviceStatus = devices.Check(owner,
            context.Request.Headers[ActiveClientDevices.HeaderName].ToString());
        if (deviceStatus != ClientDeviceStatus.Allowed)
            return ClientSessionRoutes.DeviceError(deviceStatus);

        var decision = await access.ResolveAsync(owner, targetId.Value, context.RequestAborted);
        if (decision is null)
            return Results.Forbid();
        if (!decision.CanUpload)
            return Results.Json(new { error = "file_upload_not_authorized" },
                statusCode: StatusCodes.Status403Forbidden);

        try
        {
            var bytes = await uploader.UploadAsync(decision, context.Request.Body, fileName, context.RequestAborted);
            return Results.Ok(new { target = targetId.Value, fileName, remotePath = $"~/characfile/{fileName}", bytes });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (IOException exception)
        {
            return Results.Json(new { error = exception.Message },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            Renci.SshNet.Common.SshException or IOException or TimeoutException)
        {
            logger.LogWarning(exception, "File upload failed for target {TargetId} and file {FileName}.",
                targetId, fileName);
            return Results.Json(new { error = "file_upload_failed" },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
