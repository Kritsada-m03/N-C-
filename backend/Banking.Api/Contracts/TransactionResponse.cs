using Banking.Api.Models;

namespace Banking.Api.Contracts;

public class TransactionResponse
{
    public string Message { get; set; } = "";
    public bool IsDuplicate { get; set; }
    public TransactionRecord Transaction { get; set; } = new TransactionRecord();
}
