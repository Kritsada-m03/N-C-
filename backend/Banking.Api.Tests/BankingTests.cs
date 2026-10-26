using System.Net;
using System.Net.Http.Json;
using Banking.Api.Contracts;
using Banking.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Banking.Api.Tests;

public class BankingTests
{
    [Fact]
    public async Task NewDatabaseStartsWithTenThousandBaht()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        var account = await GetAccount(client);
        Assert.Equal(10000m, account.Account.Balance);
        Assert.Empty(account.Transactions);
    }

    [Fact]
    public async Task DepositAndWithdrawKeepSatangExactly()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        var requests = new List<TransactionRequest>
        {
            NewRequest("deposit", 0.10m),
            NewRequest("deposit", 0.20m),
            NewRequest("withdraw", 0.05m)
        };
        foreach (var request in requests)
        {
            using var response = await client.PostAsJsonAsync("/api/transactions", request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        var account = await GetAccount(client);
        Assert.Equal(10000.25m, account.Account.Balance);
        Assert.Equal(3, account.Transactions.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    [InlineData(1000000001)]
    public async Task InvalidAmountDoesNotChangeBalance(double amount)
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/transactions", NewRequest("deposit", (decimal)amount));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var account = await GetAccount(client);
        Assert.Equal(10000m, account.Account.Balance);
        Assert.Empty(account.Transactions);
    }

    [Theory]
    [InlineData("requestId")]
    [InlineData("type")]
    [InlineData("amount")]
    public async Task MissingRequiredFieldIsRejected(string field)
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        var request = NewRequest();
        if (field == "requestId") request.RequestId = null;
        else if (field == "type") request.Type = null;
        else request.Amount = null;
        using var response = await client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await GetAccount(client)).Transactions);
    }

    [Fact]
    public async Task InsufficientFundsDoesNotCreateHistory()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/transactions", NewRequest("withdraw", 10000.01m));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("INSUFFICIENT_FUNDS", error!.Code);
        var account = await GetAccount(client);
        Assert.Equal(10000m, account.Account.Balance);
        Assert.Empty(account.Transactions);
    }

    [Fact]
    public async Task WithdrawingFullBalanceLeavesZero()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/transactions", NewRequest("withdraw", 10000m));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0m, (await GetAccount(client)).Account.Balance);
    }

    [Fact]
    public async Task DuplicateReturnsOriginalTransactionEvenAfterOtherTransactions()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        var request = NewRequest();
        using var first = await client.PostAsJsonAsync("/api/transactions", request);
        var original = await first.Content.ReadFromJsonAsync<TransactionResponse>();
        using var other = await client.PostAsJsonAsync("/api/transactions", NewRequest("deposit", 50m));
        request.RequestId = request.RequestId!.ToUpperInvariant();
        using var duplicate = await client.PostAsJsonAsync("/api/transactions", request);
        var replay = await duplicate.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.True(replay!.IsDuplicate);
        Assert.Equal(original!.Transaction.Id, replay.Transaction.Id);
        Assert.Equal(original.Transaction.CreatedAt, replay.Transaction.CreatedAt);
        Assert.Equal(10100m, replay.Transaction.BalanceAfter);
        var account = await GetAccount(client);
        Assert.Equal(10150m, account.Account.Balance);
        Assert.Equal(2, account.Transactions.Count);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("type")]
    [InlineData("account")]
    public async Task SameRequestIdWithDifferentDetailsReturnsConflict(string field)
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        var request = NewRequest();
        using var original = await client.PostAsJsonAsync("/api/transactions", request);
        if (field == "amount") request.Amount = 200m;
        else if (field == "type") request.Type = "withdraw";
        else request.AccountId = 2;
        using var response = await client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var account = await GetAccount(client);
        Assert.Equal(10100m, account.Account.Balance);
        Assert.Single(account.Transactions);
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsOnlyApplyOnce()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        var request = NewRequest();
        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 6; i++)
        {
            tasks.Add(Task.Run(() => client.PostAsJsonAsync("/api/transactions", request)));
        }
        var responses = await Task.WhenAll(tasks);
        int created = 0;
        int duplicates = 0;
        foreach (var response in responses)
        {
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Created) created++;
                else if (response.StatusCode == HttpStatusCode.OK) duplicates++;
                else Assert.Fail($"Unexpected status: {response.StatusCode}");
            }
        }
        Assert.Equal(1, created);
        Assert.Equal(5, duplicates);
        var account = await GetAccount(client);
        Assert.Equal(10100m, account.Account.Balance);
        Assert.Single(account.Transactions);
    }

    [Fact]
    public async Task ConcurrentWithdrawalsCannotOverdraw()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        var first = Task.Run(() => client.PostAsJsonAsync("/api/transactions", NewRequest("withdraw", 7000m)));
        var second = Task.Run(() => client.PostAsJsonAsync("/api/transactions", NewRequest("withdraw", 7000m)));
        var responses = await Task.WhenAll(first, second);
        var statuses = new List<HttpStatusCode>();
        foreach (var response in responses)
        {
            statuses.Add(response.StatusCode);
            response.Dispose();
        }
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.BadRequest, statuses);
        var account = await GetAccount(client);
        Assert.Equal(3000m, account.Account.Balance);
        Assert.Single(account.Transactions);
    }

    [Fact]
    public async Task FailedHistoryInsertRollsBackBalanceAndAllowsRetry()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        using var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False");
        connection.Open();
        // บังคับ INSERT ให้ล้มเหลวหลัง UPDATE โดยไม่ใส่ test switch ใน API
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER FailInsert BEFORE INSERT ON Transactions
            BEGIN SELECT RAISE(ABORT, 'Simulated insert failure'); END;
            """;
        command.ExecuteNonQuery();
        var request = NewRequest();
        using var failed = await client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var account = await GetAccount(client);
        Assert.Equal(10000m, account.Account.Balance);
        Assert.Empty(account.Transactions);
        command.CommandText = "DROP TRIGGER FailInsert;";
        command.ExecuteNonQuery();
        using var retry = await client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(10100m, (await GetAccount(client)).Account.Balance);
    }

    [Fact]
    public async Task ReinitializingDatabasePreservesSavedBalance()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/transactions", NewRequest());
        factory.Services.GetRequiredService<BankDatabase>().Initialize();
        var account = await GetAccount(client);
        Assert.Equal(10100m, account.Account.Balance);
        Assert.Single(account.Transactions);
    }

    [Fact]
    public async Task DatabaseUniqueConstraintRejectsDuplicateEvenWithoutService()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/transactions", NewRequest());
        using var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Transactions (RequestId, AccountId, Type, AmountSatang, BalanceAfterSatang, CreatedAt)
            SELECT RequestId, AccountId, Type, AmountSatang, BalanceAfterSatang, CreatedAt FROM Transactions;
            """;
        var exception = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(2067, exception.SqliteExtendedErrorCode);
        Assert.Single((await GetAccount(client)).Transactions);
    }

    [Fact]
    public async Task NonexistentAccountReturnsNotFound()
    {
        using var factory = new BankingApiFactory();
        using var client = factory.CreateClient();
        using var get = await client.GetAsync("/api/accounts/999");
        var request = NewRequest();
        request.AccountId = 999;
        using var post = await client.PostAsJsonAsync("/api/transactions", request);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Empty((await GetAccount(client)).Transactions);
    }

    private static TransactionRequest NewRequest(string type = "deposit", decimal amount = 100m)
    {
        return new TransactionRequest
        {
            AccountId = 1,
            RequestId = Guid.NewGuid().ToString(),
            Type = type,
            Amount = amount
        };
    }

    private static async Task<AccountResponse> GetAccount(HttpClient client)
    {
        return (await client.GetFromJsonAsync<AccountResponse>("/api/accounts/1"))!;
    }
}
