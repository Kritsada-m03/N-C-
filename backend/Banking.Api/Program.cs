using Banking.Api.Contracts;
using Banking.Api.Data;
using Banking.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<BankDatabase>();
builder.Services.AddScoped<BankingService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();
app.Services.GetRequiredService<BankDatabase>().Initialize();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var error = new ApiError();

        if (exception is BankingException bankingException)
        {
            context.Response.StatusCode = bankingException.StatusCode;
            error.Code = bankingException.Code;
            error.Message = bankingException.Message;
        }
        else if (exception is SqliteException sqliteException &&
                 (sqliteException.SqliteErrorCode == 5 || sqliteException.SqliteErrorCode == 6))
        {
            context.Response.StatusCode = 503;
            context.Response.Headers.RetryAfter = "1";
            error.Code = "DATABASE_BUSY";
            error.Message = "ฐานข้อมูลกำลังทำรายการ กรุณาลองอีกครั้งด้วย Request ID เดิม";
        }
        else
        {
            context.Response.StatusCode = 500;
            error.Code = "INTERNAL_ERROR";
            error.Message = "ทำรายการไม่สำเร็จ กรุณาลองอีกครั้งด้วย Request ID เดิม";
        }

        await context.Response.WriteAsJsonAsync(error);
    });
});

app.UseCors();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.Run();

// ให้ integration tests เปิด API จริงผ่าน WebApplicationFactory ได้
public partial class Program { }
