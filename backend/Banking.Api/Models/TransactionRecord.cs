namespace Banking.Api.Models;

public class TransactionRecord
{
    public long Id { get; set; }
    public string RequestId { get; set; } = "";
    public int AccountId { get; set; }
    public string Type { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string CreatedAt { get; set; } = "";
}
