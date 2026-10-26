using Banking.Api.Contracts;
using Banking.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Banking.Api.Controllers;

[ApiController]
[Route("api/transactions")]
public class TransactionsController : ControllerBase
{
    private readonly BankingService service;

    public TransactionsController(BankingService service)
    {
        this.service = service;
    }

    [HttpPost]
    public ActionResult<TransactionResponse> CreateTransaction(TransactionRequest request)
    {
        var result = service.CreateTransaction(request);

        if (result.IsDuplicate)
        {
            return Ok(result);
        }
        else
        {
            return StatusCode(StatusCodes.Status201Created, result);
        }
    }
}
