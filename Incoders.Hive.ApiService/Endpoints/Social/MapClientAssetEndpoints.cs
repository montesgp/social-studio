using Incoders.Hive.ApiService.Endpoints.Common;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Application.Abstractions.Tenancy;
using Incoders.Hive.Domain.Common;
using Incoders.Hive.Domain.Social.Clients;

namespace Incoders.Hive.ApiService.Endpoints.Social;

public static class ClientAssetEndpointsExtensions
{
    public static IEndpointRouteBuilder MapClientAssetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/social/clients/{clientId:guid}/assets")
            .WithTags("Client Assets")
            .RequireAuthorization();

        group.MapPost("/images", UploadImageAsync)
            .WithName("UploadClientImageAsset")
            .WithSummary("Upload an image asset for a client")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ClientImageAssetUploadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return app;
    }

    private static async Task<IResult> UploadImageAsync(
        Guid clientId,
        HttpRequest request,
        ICurrentTenant currentTenant,
        ISocialClientRepository clients,
        IClientAssetStorage storage,
        CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not { } organizationId)
        {
            return ClientErrors.OrganizationRequired.ToProblem();
        }

        var client = await clients.GetByIdAsync(new ClientId(clientId), organizationId.Value, cancellationToken);
        if (client is null)
        {
            return ClientErrors.NotFound.ToProblem();
        }

        if (!request.HasFormContentType)
        {
            return TypedResults.Problem(
                title: "Validation failed",
                detail: "multipart/form-data content type is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "social.client_assets.invalid_form_data" });
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return TypedResults.Problem(
                title: "Validation failed",
                detail: "Form field 'file' is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "social.client_assets.file_required" });
        }

        await using var stream = file.OpenReadStream();
        var uploadResult = await storage.UploadImageAsync(
            new ClientAssetUploadRequest(
                OrganizationId: organizationId.Value,
                ClientId: clientId,
                ContentType: file.ContentType ?? string.Empty,
                OriginalFileName: file.FileName,
                Content: stream,
                SizeBytes: file.Length),
            cancellationToken);

        if (uploadResult.IsFailure)
        {
            return MapUploadError(uploadResult.Error);
        }

        var upload = uploadResult.Value;
        var absoluteUrl = $"{request.Scheme}://{request.Host}{upload.PublicPath}";

        return TypedResults.Ok(new ClientImageAssetUploadResponse(
            Url: absoluteUrl,
            PublicPath: upload.PublicPath,
            StoredFileName: upload.StoredFileName,
            ContentType: upload.ContentType,
            SizeBytes: upload.SizeBytes,
            OriginalFileName: upload.OriginalFileName));
    }

    private static IResult MapUploadError(Error error)
    {
        if (error.Code == ClientAssetStorageErrors.FileTooLarge.Code)
        {
            return TypedResults.Problem(
                title: "Payload too large",
                detail: error.MessageKey,
                statusCode: StatusCodes.Status413PayloadTooLarge,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }

        if (error.Code == ClientAssetStorageErrors.InvalidContentType.Code)
        {
            return TypedResults.Problem(
                title: "Unsupported media type",
                detail: error.MessageKey,
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }

        return error.ToProblem();
    }
}

public sealed record ClientImageAssetUploadResponse(
    string Url,
    string PublicPath,
    string StoredFileName,
    string ContentType,
    long SizeBytes,
    string OriginalFileName);
