namespace Banking.Api.Contracts;

public class TransactionRequest
{
    public int AccountId { get; set; }
    public string? RequestId { get; set; }
    public string? Type { get; set; }
    public decimal? Amount { get; set; }
}
