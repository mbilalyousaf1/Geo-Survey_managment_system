namespace GeoSurvey.Organizations.Domain;

public sealed class Organization
{
    public int Id { get; private set; }
    public Guid PublicId { get; private set; }
    public OrganizationCode Code { get; private set; }
    public string DisplayName { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Organization(int id, Guid publicId, OrganizationCode code, string displayName, DateTimeOffset createdAtUtc)
    {
        Id = id;
        PublicId = publicId;
        Code = code;
        DisplayName = displayName;
        CreatedAtUtc = createdAtUtc;
    }

    public static Organization Create(OrganizationCode code, string displayName)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(displayName);

        var trimmedName = displayName.Trim();
        if (trimmedName.Length is < 1 or > 200)
            throw new ArgumentException("Organization display name must contain 1-200 characters after trimming.", nameof(displayName));

        return new Organization(0, Guid.NewGuid(), code, trimmedName, DateTimeOffset.UtcNow);
    }
}
