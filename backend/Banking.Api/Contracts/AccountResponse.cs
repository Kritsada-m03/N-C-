using Banking.Api.Models;

namespace Banking.Api.Contracts;

public class AccountResponse
{
    public Account Account { get; set; } = new Account();
    public List<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();
}
