using Incoders.Hive.Application.Abstractions;
using Incoders.Hive.Application.Abstractions.Social;
using Incoders.Hive.Domain.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Incoders.Hive.Infrastructure.Social;

public sealed class LocalClientAssetStorage : IClientAssetStorage
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypeToExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/jpg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
        };

    private readonly ClientAssetStorageOptions _options;
    private readonly string _rootPath;
    private readonly string _publicBasePath;
    private readonly IClock _clock;

    public LocalClientAssetStorage(
        IOptions<ClientAssetStorageOptions> options,
        IHostEnvironment hostEnvironment,
        IClock clock)
    {
        _options = options.Value;
        _rootPath = _options.ResolveRootPath(hostEnvironment.ContentRootPath);
        _publicBasePath = _options.NormalizedPublicBasePath();
        _clock = clock;
    }

    public async Task<Result<ClientAssetUploadResult>> UploadImageAsync(
        ClientAssetUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SizeBytes <= 0)
        {
            return Result.Failure<ClientAssetUploadResult>(ClientAssetStorageErrors.EmptyFile);
        }

        if (request.SizeBytes > _options.MaxImageSizeBytes)
        {
            return Result.Failure<ClientAssetUploadResult>(ClientAssetStorageErrors.FileTooLarge);
        }

        if (!_options.AllowedImageContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<ClientAssetUploadResult>(ClientAssetStorageErrors.InvalidContentType);
        }

        if (!ContentTypeToExtension.TryGetValue(request.ContentType, out var extension))
        {
            return Result.Failure<ClientAssetUploadResult>(ClientAssetStorageErrors.InvalidContentType);
        }

        var now = _clock.UtcNow;
        var storedFileName = $"{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{extension}";

        var relativePath = Path.Combine(
            request.OrganizationId.ToString("N"),
            request.ClientId.ToString("N"),
            now.ToString("yyyy"),
            now.ToString("MM"),
            storedFileName);

        var absolutePath = Path.Combine(_rootPath, relativePath);
        var directory = Path.GetDirectoryName(absolutePath);
        if (directory is null)
        {
            throw new InvalidOperationException("Could not resolve upload directory.");
        }

        Directory.CreateDirectory(directory);

        await using (var target = new FileStream(
                         absolutePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         useAsync: true))
        {
            await request.Content.CopyToAsync(target, cancellationToken);
        }

        var normalizedRelativePath = relativePath.Replace('\\', '/');
        var publicPath = $"{_publicBasePath}/{normalizedRelativePath}";

        return Result.Success(new ClientAssetUploadResult(
            PublicPath: publicPath,
            StoredFileName: storedFileName,
            ContentType: request.ContentType,
            SizeBytes: request.SizeBytes,
            OriginalFileName: request.OriginalFileName));
    }

    public Task DeleteClientAssetsAsync(
        Guid organizationId,
        Guid clientId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clientFolder = Path.Combine(
            _rootPath,
            organizationId.ToString("N"),
            clientId.ToString("N"));

        var rootFullPath = Path.GetFullPath(_rootPath);
        var clientFullPath = Path.GetFullPath(clientFolder);

        if (!clientFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved client asset folder is outside the configured storage root.");
        }

        if (Directory.Exists(clientFullPath))
        {
            Directory.Delete(clientFullPath, recursive: true);
        }

        return Task.CompletedTask;
    }
}
