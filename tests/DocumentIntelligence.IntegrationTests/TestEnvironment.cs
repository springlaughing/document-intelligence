using Azure.Messaging.ServiceBus;
using Microsoft.Data.SqlClient;

namespace DocumentIntelligence.IntegrationTests;

// These tests need a real broker and a real relational database, because the things they
// cover - deserialization on the wire, the settle matrix, a unique constraint enforcing
// idempotency - are exactly what fakes cannot reproduce.
//
// Neither is assumed to be present. Each test asks first and skips if not, so running the
// suite on a machine with no containers reports "skipped" rather than failing for a
// reason that has nothing to do with the code.
public static class TestEnvironment
{
    public static string ServiceBusConnectionString =>
        Environment.GetEnvironmentVariable("TEST_SERVICEBUS_CONNECTION")
        ?? "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    public static string SqlConnectionString =>
        Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
        ?? @"Server=(localdb)\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True";

    public static async Task<bool> BrokerIsReachableAsync(string entityName)
    {
        try
        {
            await using var client = new ServiceBusClient(ServiceBusConnectionString);
            await using var sender = client.CreateSender(entityName);

            // CreateSender is lazy, so force the link open. If the entity is missing or
            // nothing is listening this throws rather than silently succeeding.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var batch = await sender.CreateMessageBatchAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> SqlIsReachableAsync()
    {
        try
        {
            await using var conn = new SqlConnection(SqlConnectionString);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await conn.OpenAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// A database per test run, so runs cannot interfere with each other or with the app.
    public static string CreateTestDatabaseConnectionString(out string databaseName)
    {
        databaseName = "DocIntegrationTests_" + Guid.NewGuid().ToString("N")[..12];

        var builder = new SqlConnectionStringBuilder(SqlConnectionString)
        {
            InitialCatalog = databaseName
        };

        return builder.ConnectionString;
    }

    public static async Task DropDatabaseAsync(string databaseName)
    {
        try
        {
            await using var conn = new SqlConnection(SqlConnectionString);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                IF DB_ID('{databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{databaseName}];
                END
                """;
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Cleanup failure must not fail the test it is cleaning up after.
        }
    }
}
