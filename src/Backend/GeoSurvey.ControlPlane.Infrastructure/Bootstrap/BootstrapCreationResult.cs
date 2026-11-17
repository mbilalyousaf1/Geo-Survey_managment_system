namespace GeoSurvey.ControlPlane.Infrastructure.Bootstrap;

public enum BootstrapCreationResult
{
    Created,
    AlreadyCompleted,
    AdministratorAlreadyExists,
    AccountAlreadyExists,
    LockUnavailable
}
