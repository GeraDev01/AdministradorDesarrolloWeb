namespace AdminWeb.Domain.Entities;

public class DeploymentProfileTarget
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int TargetId { get; set; }
    public int Order { get; set; } = 0;

    public DeploymentProfile Profile { get; set; } = null!;
    public DeploymentTarget Target { get; set; } = null!;
}
