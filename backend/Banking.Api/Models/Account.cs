namespace Banking.Api.Models;

public class Account
{
    public int Id { get; set; }
    public string AccountNumber { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public decimal Balance { get; set; }
}
