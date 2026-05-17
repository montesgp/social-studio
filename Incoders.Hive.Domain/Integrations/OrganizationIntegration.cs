using Incoders.Hive.Domain.Common;

namespace Incoders.Hive.Domain.Integrations;

public sealed class OrganizationIntegration : Entity<OrganizationIntegrationId>
{
    public const int SelectedAiModelMaxLength = 200;

    private OrganizationIntegration(
        OrganizationIntegrationId id,
        Guid organizationId,
        IntegrationProviderId providerId,
        IntegrationConnectionMode connectionMode,
        IntegrationStatus status,
        bool isReadOnly,
        bool isDefaultAi,
        string? selectedAiModel,
        DateTime? lastTestedAtUtc,
        string? lastError,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
        : base(id)
    {
        OrganizationId = organizationId;
        ProviderId = providerId;
        ConnectionMode = connectionMode;
        Status = status;
        IsReadOnly = isReadOnly;
        IsDefaultAi = isDefaultAi;
        SelectedAiModel = selectedAiModel;
        LastTestedAtUtc = lastTestedAtUtc;
        LastError = lastError;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid OrganizationId { get; private set; }

    public IntegrationProviderId ProviderId { get; private set; }

    public IntegrationConnectionMode ConnectionMode { get; private set; }

    public IntegrationStatus Status { get; private set; }

    public bool IsReadOnly { get; private set; }

    public bool IsDefaultAi { get; private set; }

    public string? SelectedAiModel { get; private set; }

    public DateTime? LastTestedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<OrganizationIntegration> Create(
        OrganizationIntegrationId id,
        Guid organizationId,
        IntegrationProviderId providerId,
        IntegrationConnectionMode connectionMode,
        IntegrationStatus status,
        bool isReadOnly,
        bool isDefaultAi,
        string? selectedAiModel,
        DateTime? lastTestedAtUtc,
        string? lastError,
        DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
        {
            return Result.Failure<OrganizationIntegration>(IntegrationErrors.OrganizationRequired);
        }

        if (providerId.Value == Guid.Empty)
        {
            return Result.Failure<OrganizationIntegration>(IntegrationErrors.ProviderNotFound);
        }

        if (isReadOnly && connectionMode != IntegrationConnectionMode.SystemDefault)
        {
            return Result.Failure<OrganizationIntegration>(IntegrationErrors.ReadOnlySystemDefaultOnly);
        }

        if (isDefaultAi && status != IntegrationStatus.Connected)
        {
            return Result.Failure<OrganizationIntegration>(IntegrationErrors.DefaultAiProviderNotConnected);
        }

        if (!string.IsNullOrWhiteSpace(selectedAiModel) && selectedAiModel.Trim().Length > SelectedAiModelMaxLength)
        {
            return Result.Failure<OrganizationIntegration>(IntegrationErrors.DefaultAiProviderNotConnected);
        }

        return Result.Success(new OrganizationIntegration(
            id,
            organizationId,
            providerId,
            connectionMode,
            status,
            isReadOnly,
            isDefaultAi,
            NormalizeModel(selectedAiModel),
            lastTestedAtUtc,
            string.IsNullOrWhiteSpace(lastError) ? null : lastError.Trim(),
            nowUtc,
            nowUtc));
    }

    public static OrganizationIntegration Restore(
        OrganizationIntegrationId id,
        Guid organizationId,
        IntegrationProviderId providerId,
        IntegrationConnectionMode connectionMode,
        IntegrationStatus status,
        bool isReadOnly,
        bool isDefaultAi,
        string? selectedAiModel,
        DateTime? lastTestedAtUtc,
        string? lastError,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        return new OrganizationIntegration(
            id,
            organizationId,
            providerId,
            connectionMode,
            status,
            isReadOnly,
            isDefaultAi,
            selectedAiModel,
            lastTestedAtUtc,
            lastError,
            createdAtUtc,
            updatedAtUtc);
    }

    public Result Configure(
        IntegrationConnectionMode mode,
        bool isReadOnly,
        string? selectedAiModel,
        bool enabled,
        DateTime nowUtc)
    {
        if (isReadOnly && mode != IntegrationConnectionMode.SystemDefault)
        {
            return Result.Failure(IntegrationErrors.ReadOnlySystemDefaultOnly);
        }

        if (!string.IsNullOrWhiteSpace(selectedAiModel) && selectedAiModel.Trim().Length > SelectedAiModelMaxLength)
        {
            return Result.Failure(IntegrationErrors.DefaultAiProviderNotConnected);
        }

        ConnectionMode = mode;
        IsReadOnly = isReadOnly;
        SelectedAiModel = NormalizeModel(selectedAiModel);
        LastError = null;
        UpdatedAtUtc = nowUtc;

        if (!enabled)
        {
            Status = IntegrationStatus.Disabled;
            IsDefaultAi = false;
            return Result.Success();
        }

        if (Status == IntegrationStatus.Disabled)
        {
            Status = IntegrationStatus.NotConfigured;
        }

        return Result.Success();
    }

    public void MarkTestResult(bool isSuccess, string? error, DateTime nowUtc)
    {
        Status = isSuccess ? IntegrationStatus.Connected : IntegrationStatus.Failed;
        LastTestedAtUtc = nowUtc;
        LastError = isSuccess ? null : (string.IsNullOrWhiteSpace(error) ? IntegrationErrors.ConnectionTestFailed.MessageKey : error.Trim());
        UpdatedAtUtc = nowUtc;

        if (!isSuccess)
        {
            IsDefaultAi = false;
        }
    }

    public Result SetDefaultAi(string? model, DateTime nowUtc)
    {
        if (Status != IntegrationStatus.Connected)
        {
            return Result.Failure(IntegrationErrors.DefaultAiProviderNotConnected);
        }

        if (!string.IsNullOrWhiteSpace(model) && model.Trim().Length > SelectedAiModelMaxLength)
        {
            return Result.Failure(IntegrationErrors.DefaultAiProviderNotConnected);
        }

        IsDefaultAi = true;
        SelectedAiModel = NormalizeModel(model) ?? SelectedAiModel;
        UpdatedAtUtc = nowUtc;

        return Result.Success();
    }

    public void ClearDefaultAi(DateTime nowUtc)
    {
        IsDefaultAi = false;
        UpdatedAtUtc = nowUtc;
    }

    private static string? NormalizeModel(string? model)
    {
        return string.IsNullOrWhiteSpace(model) ? null : model.Trim();
    }
}