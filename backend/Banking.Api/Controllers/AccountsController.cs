using Banking.Api.Contracts;
using Banking.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Banking.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly BankingService service;

    public AccountsController(BankingService service)
    {
        this.service = service;
    }

    [HttpGet("{id:int}")]
    public ActionResult<AccountResponse> GetAccount(int id)
    {
        return Ok(service.GetAccount(id));
    }
}
