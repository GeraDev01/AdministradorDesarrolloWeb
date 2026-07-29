namespace Administrador_Desarrollo_Web.Models;

public enum AzureResourceType
{
    AppService        = 0,
    SqlDatabase       = 1,
    StorageAccount    = 2,
    KeyVault          = 3,
    FunctionApp       = 4,
    ContainerRegistry = 5,
    ServiceBus        = 6,
    CosmosDb          = 7,
    RedisCache        = 8,
    VirtualMachine    = 9,
    Other             = 10
}

public enum AzureResourceStatus
{
    EnUso    = 0,
    NoUsado  = 1,
    EnPrueba = 2,
    Archivado = 3
}

public enum AzureEnvironment
{
    Produccion  = 0,
    Staging     = 1,
    Desarrollo  = 2,
    Compartido  = 3
}

public class AzureResource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AzureResourceType ResourceType { get; set; }
    public AzureResourceStatus Status { get; set; }
    public AzureEnvironment Environment { get; set; }
    public string? ResourceGroup { get; set; }
    public string? SubscriptionName { get; set; }
    public string? Region { get; set; }
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public decimal? MonthlyCostEstimate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
