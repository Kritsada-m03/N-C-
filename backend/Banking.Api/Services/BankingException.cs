namespace Banking.Api.Services;

public class BankingException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public BankingException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}
