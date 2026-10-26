using Banking.Api.Contracts;
using Banking.Api.Data;
using Banking.Api.Models;
using Microsoft.Data.Sqlite;

namespace Banking.Api.Services;

public class BankingService
{
    public const decimal MaximumMoney = 1_000_000_000m;
    private readonly BankDatabase database;

    public BankingService(BankDatabase database)
    {
        this.database = database;
    }

    public AccountResponse GetAccount(int accountId)
    {
        using var connection = database.OpenConnection();
        // อ่านยอดกับประวัติจาก snapshot เดียวกัน
        using var transaction = connection.BeginTransaction(deferred: true);
        var account = ReadAccount(connection, transaction, accountId);
        var records = new List<TransactionRecord>();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT * FROM Transactions
            WHERE AccountId = $accountId ORDER BY Id DESC LIMIT 50;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                records.Add(ReadTransaction(reader));
            }
        }

        transaction.Commit();
        return new AccountResponse { Account = account, Transactions = records };
    }

    public TransactionResponse CreateTransaction(TransactionRequest request)
    {
        ValidateRequest(request);

        string requestId = Guid.Parse(request.RequestId!).ToString("D");
        decimal amount = request.Amount!.Value;
        long amountSatang = (long)(amount * 100m);

        using var connection = database.OpenConnection();

        // BEGIN IMMEDIATE: ล็อกสิทธิ์เขียนก่อนอ่านยอด ป้องกันถอนพร้อมกันแล้วใช้ยอดเก่า
        // ทุก request ใช้ connection ของตัวเอง และรอ writer ก่อนหน้าได้สูงสุด 10 วินาที
        using var transaction = connection.BeginTransaction(deferred: false);

        try
        {
            // ต้องตรวจซ้ำภายใน transaction และยังมี UNIQUE ที่ database ป้องกันอีกชั้น
            var existing = FindTransaction(connection, transaction, requestId);
            if (existing != null)
            {
                if (existing.AccountId != request.AccountId ||
                    existing.Type != request.Type || existing.Amount != amount)
                {
                    throw new BankingException(409, "REQUEST_ID_CONFLICT",
                        "Request ID นี้ถูกใช้กับรายละเอียดรายการอื่นแล้ว");
                }

                transaction.Commit();
                return new TransactionResponse
                {
                    IsDuplicate = true,
                    Message = "รายการนี้สำเร็จไปแล้ว คืนผลเดิมโดยไม่ปรับยอดเงินซ้ำ",
                    Transaction = existing
                };
            }

            var account = ReadAccount(connection, transaction, request.AccountId);
            long currentBalance = (long)(account.Balance * 100m);
            long newBalance;

            if (request.Type == "deposit")
            {
                newBalance = currentBalance + amountSatang;
                if (newBalance > (long)(MaximumMoney * 100m))
                {
                    throw new BankingException(400, "BALANCE_LIMIT",
                        "ยอดเงินหลังฝากต้องไม่เกิน 1,000,000,000 บาท");
                }
            }
            else
            {
                if (amountSatang > currentBalance)
                {
                    throw new BankingException(400, "INSUFFICIENT_FUNDS", "ยอดเงินไม่พอสำหรับถอน");
                }

                newBalance = currentBalance - amountSatang;
            }

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Accounts SET BalanceSatang = $balance WHERE Id = $id;";
            update.Parameters.AddWithValue("$balance", newBalance);
            update.Parameters.AddWithValue("$id", request.AccountId);
            update.ExecuteNonQuery();

            string createdAt = DateTimeOffset.UtcNow.ToString("O");
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Transactions
                    (RequestId, AccountId, Type, AmountSatang, BalanceAfterSatang, CreatedAt)
                VALUES ($requestId, $accountId, $type, $amount, $balance, $createdAt)
                RETURNING Id;
                """;
            insert.Parameters.AddWithValue("$requestId", requestId);
            insert.Parameters.AddWithValue("$accountId", request.AccountId);
            insert.Parameters.AddWithValue("$type", request.Type!);
            insert.Parameters.AddWithValue("$amount", amountSatang);
            insert.Parameters.AddWithValue("$balance", newBalance);
            insert.Parameters.AddWithValue("$createdAt", createdAt);
            long id = (long)insert.ExecuteScalar()!;

            // ยอดเงิน + ประวัติ ต้องสำเร็จพร้อมกัน จึงค่อย commit
            transaction.Commit();

            return new TransactionResponse
            {
                Message = "ทำรายการสำเร็จ",
                IsDuplicate = false,
                Transaction = new TransactionRecord
                {
                    Id = id,
                    RequestId = requestId,
                    AccountId = request.AccountId,
                    Type = request.Type!,
                    Amount = amount,
                    BalanceAfter = newBalance / 100m,
                    CreatedAt = createdAt
                }
            };
        }
        catch
        {
            // เช่น UPDATE สำเร็จ แต่ INSERT ล้มเหลว: ย้อนยอดเงินกลับด้วย
            transaction.Rollback();
            throw;
        }
    }

    private static void ValidateRequest(TransactionRequest request)
    {
        if (request.AccountId <= 0)
        {
            throw new BankingException(400, "INVALID_ACCOUNT", "กรุณาระบุบัญชีให้ถูกต้อง");
        }

        if (!Guid.TryParse(request.RequestId, out var requestId) || requestId == Guid.Empty)
        {
            throw new BankingException(400, "INVALID_REQUEST_ID", "Request ID ต้องเป็น GUID และห้ามเป็นค่าว่าง");
        }

        if (request.Type != "deposit" && request.Type != "withdraw")
        {
            throw new BankingException(400, "INVALID_TYPE", "ประเภทรายการต้องเป็น deposit หรือ withdraw");
        }

        if (request.Amount == null || request.Amount <= 0 || request.Amount > MaximumMoney)
        {
            throw new BankingException(400, "INVALID_AMOUNT", "จำนวนเงินต้องมากกว่า 0 และไม่เกิน 1,000,000,000 บาท");
        }

        if (decimal.Round(request.Amount.Value, 2) != request.Amount.Value)
        {
            throw new BankingException(400, "INVALID_PRECISION", "จำนวนเงินมีทศนิยมได้ไม่เกิน 2 ตำแหน่ง");
        }
    }

    private static Account ReadAccount(SqliteConnection connection, SqliteTransaction transaction, int accountId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM Accounts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", accountId);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new BankingException(404, "ACCOUNT_NOT_FOUND", "ไม่พบบัญชีนี้");
        }

        return new Account
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            AccountNumber = reader.GetString(reader.GetOrdinal("AccountNumber")),
            OwnerName = reader.GetString(reader.GetOrdinal("OwnerName")),
            Balance = reader.GetInt64(reader.GetOrdinal("BalanceSatang")) / 100m
        };
    }

    private static TransactionRecord? FindTransaction(SqliteConnection connection, SqliteTransaction transaction, string requestId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM Transactions WHERE RequestId = $requestId;";
        command.Parameters.AddWithValue("$requestId", requestId);
        using var reader = command.ExecuteReader();

        if (reader.Read())
        {
            return ReadTransaction(reader);
        }

        return null;
    }

    private static TransactionRecord ReadTransaction(SqliteDataReader reader)
    {
        return new TransactionRecord
        {
            Id = reader.GetInt64(reader.GetOrdinal("Id")),
            RequestId = reader.GetString(reader.GetOrdinal("RequestId")),
            AccountId = reader.GetInt32(reader.GetOrdinal("AccountId")),
            Type = reader.GetString(reader.GetOrdinal("Type")),
            Amount = reader.GetInt64(reader.GetOrdinal("AmountSatang")) / 100m,
            BalanceAfter = reader.GetInt64(reader.GetOrdinal("BalanceAfterSatang")) / 100m,
            CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt"))
        };
    }
}
