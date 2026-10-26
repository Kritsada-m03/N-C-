using Microsoft.Data.Sqlite;

namespace Banking.Api.Data;

public class BankDatabase
{
    private readonly string databasePath;
    private readonly string schemaPath;

    public BankDatabase(IConfiguration configuration, IWebHostEnvironment environment)
    {
        string configuredPath = configuration["Database:Path"] ?? "Data/banking.db";
        databasePath = Path.GetFullPath(configuredPath, environment.ContentRootPath);
        schemaPath = Path.Combine(environment.ContentRootPath, "Data", "schema.sql");
    }

    public SqliteConnection OpenConnection()
    {
        var settings = new SqliteConnectionStringBuilder();
        settings.DataSource = databasePath;
        settings.ForeignKeys = true;
        settings.DefaultTimeout = 10;
        settings.Pooling = false;

        var connection = new SqliteConnection(settings.ToString());
        connection.Open();
        return connection;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = File.ReadAllText(schemaPath);
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
