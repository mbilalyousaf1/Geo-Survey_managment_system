namespace GeoSurvey.Organizations.Application;

public sealed record RegionDto(string Key, string DisplayName);

public sealed record CreateOrganizationRequest(string Code, string DisplayName, string RegionKey, string AdministratorEmail);

public sealed record OrganizationDto(Guid PublicId, string Code, string DisplayName, string RegionKey, string State,
    string AdministratorEmail, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string? FailureCode);

public sealed record AdministratorInvitationDto(string Token, DateTimeOffset ExpiresAtUtc, string OrganizationCode, string AdministratorEmail);

public sealed record ActivationInspectionDto(string OrganizationCode, string OrganizationName, string AdministratorEmail, DateTimeOffset ExpiresAtUtc);

public sealed record ActivationCompletionDto(string OrganizationCode, string AdministratorEmail);

public sealed record OrganizationPageDto(IReadOnlyList<OrganizationDto> Items, int TotalCount, int PageNumber, int PageSize);

/// <summary>Public application boundary; public DTOs never contain database targets or internal keys.</summary>
public interface IOrganizationManagement
{
    IReadOnlyList<RegionDto> GetRegions();
    Task<OrganizationPageDto> ListAsync(int pageNumber = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<OrganizationDto?> GetAsync(Guid publicId, CancellationToken cancellationToken = default);
    Task<OrganizationDto> CreateAsync(CreateOrganizationRequest request, Guid actorPublicId, CancellationToken cancellationToken = default);
    Task<OrganizationDto> RetryAsync(Guid publicId, Guid actorPublicId, CancellationToken cancellationToken = default);
    Task<AdministratorInvitationDto> IssueInvitationAsync(Guid publicId, Guid actorPublicId, CancellationToken cancellationToken = default);
    Task<ActivationInspectionDto?> InspectActivationAsync(string? token, CancellationToken cancellationToken = default);
    Task<ActivationCompletionDto?> CompleteActivationAsync(string? token, string? password, CancellationToken cancellationToken = default);
}

/// <summary>Safe public business error. Never attach raw database/provider exceptions.</summary>
public sealed class OrganizationOperationException(string code, string message, int statusCode = 409) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
