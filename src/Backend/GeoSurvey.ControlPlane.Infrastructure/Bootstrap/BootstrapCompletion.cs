namespace GeoSurvey.ControlPlane.Infrastructure.Bootstrap;

/// <summary>Durable one-time completion history, independent of any administrator account.</summary>
public sealed class BootstrapCompletion
{
    public int Id { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }

    private BootstrapCompletion(int id, DateTimeOffset completedAtUtc)
    {
        Id = id;
        CompletedAtUtc = completedAtUtc;
    }

    public static BootstrapCompletion Create() => new(1, DateTimeOffset.UtcNow);
}
