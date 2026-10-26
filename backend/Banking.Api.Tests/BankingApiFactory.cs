using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Banking.Api.Tests;

public class BankingApiFactory : WebApplicationFactory<Program>
{
    // แต่ละ test ใช้ database คนละไฟล์ ไม่แตะบัญชีที่ใช้บนหน้าจอ
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"banking-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Path", DatabasePath);
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(DatabasePath))
        {
            File.Delete(DatabasePath);
        }
    }
}
