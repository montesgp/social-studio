using SocialStudio.Domain.Common;

namespace SocialStudio.Application.Abstractions.Social;

public interface IClientAssetStorage
{
    Task<Result<ClientAssetUploadResult>> UploadImageAsync(
        ClientAssetUploadRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteClientAssetsAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default);
}

public sealed record ClientAssetUploadRequest(
    Guid OrganizationId,
    Guid ClientId,
    string ContentType,
    string OriginalFileName,
    Stream Content,
    long SizeBytes);

public sealed record ClientAssetUploadResult(
    string PublicPath,
    string StoredFileName,
    string ContentType,
    long SizeBytes,
    string OriginalFileName);

public static class ClientAssetStorageErrors
{
    public static readonly Error EmptyFile = Error.Validation(
        "social.client_assets.empty_file",
        "social.client_assets.errors.empty_file");

    public static readonly Error InvalidContentType = Error.Validation(
        "social.client_assets.invalid_content_type",
        "social.client_assets.errors.invalid_content_type");

    public static readonly Error FileTooLarge = Error.Validation(
        "social.client_assets.file_too_large",
        "social.client_assets.errors.file_too_large");
}
