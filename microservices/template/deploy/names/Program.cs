#pragma warning disable IDE0047
using System.Text.Json;
using ProjectTrackerTemplate.SharedKernel;
using Trellis.Mediator;
using Trellis.ResourceNaming;
using Trellis.ResourceNaming.Azure;

if (args.Length != 5)
    throw new ArgumentException("Usage: names <system> <environment> <cloud> <region> <region-short>");
var context = Context();
var contracts = IntegrationEventNameMap.FromAssemblies(typeof(MemberInvitedIntegrationEvent).Assembly);
var names = new Dictionary<string, string>
{
    ["resourceGroup"] = context.ResourceGroupName(),
    ["cosmosAccountName"] = context.CosmosName(),
    ["idempotencyDatabaseName"] = context.Name(new ResourceTypeSpec("idemdb", 1, 255, NameSeparator.Dash, IsDnsGlobal: false)),
    ["logAnalyticsName"] = context.LogAnalyticsName(),
    ["applicationInsightsName"] = context.Name(AzureResourceTypes.ApplicationInsights, region: args[4]),
    ["serviceBusName"] = context.ServiceBusName(),
    ["containerEnvironmentName"] = context.Name(new ResourceTypeSpec("cae", 2, 60, NameSeparator.Dash, IsDnsGlobal: false), region: args[4]),
    ["gatewayName"] = ApplicationName("gw"),
    ["membersName"] = ApplicationName("mbr"),
    ["projectsName"] = ApplicationName("prj"),
    ["gatewayIdentityName"] = Context("gw").ManagedIdentityName(),
    ["membersIdentityName"] = Context("mbr").ManagedIdentityName(),
    ["projectsIdentityName"] = Context("prj").ManagedIdentityName(),
#if (UsePostgres)
    ["databaseServerName"] = context.Name(new ResourceTypeSpec("psql", 3, 63, NameSeparator.Dash, IsDnsGlobal: true)),
    ["membersDatabaseName"] = Context("mbr").Name(new ResourceTypeSpec("pgdb", 1, 63, NameSeparator.Dash, IsDnsGlobal: false)),
    ["projectsDatabaseName"] = Context("prj").Name(new ResourceTypeSpec("pgdb", 1, 63, NameSeparator.Dash, IsDnsGlobal: false)),
#else
    ["databaseServerName"] = context.SqlServerName(),
    ["membersDatabaseName"] = Context("mbr").Name(AzureResourceTypes.SqlDatabase),
    ["projectsDatabaseName"] = Context("prj").Name(AzureResourceTypes.SqlDatabase),
#endif
    ["topicName"] = contracts.NameFor(typeof(MemberInvitedIntegrationEvent))
        .GetValueOrThrow("MemberInvited must declare an IntegrationEventName."),
    ["subscriptionName"] = MessagingTopology.ProjectsSubscriptionName,
};
Console.WriteLine(JsonSerializer.Serialize(names, new JsonSerializerOptions { WriteIndented = true }));

DeployedEnvironmentOptions Context(string? service = null) => new()
{
    System = args[0],
    Environment = args[1],
    Cloud = args[2],
    Region = args[3],
    RegionShortName = args[4],
    Service = service,
    Scope = CloudScope.Shared,
};

string ApplicationName(string service) => Context(service).Name(
    new ResourceTypeSpec("ca", 2, 32, NameSeparator.Dash, IsDnsGlobal: false), region: args[4]);
