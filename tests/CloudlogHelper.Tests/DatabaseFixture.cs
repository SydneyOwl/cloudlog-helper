using CloudlogHelper.Services;
using CloudlogHelper.Services.Interfaces;

namespace CloudlogHelper.Tests;

public class DatabaseFixture : IAsyncLifetime
{
    public IDatabaseService DatabaseService { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        DatabaseService = new DatabaseService();
        await DatabaseService.InitDatabaseAsync(":memory:", true);
        await DatabaseService.UpgradeDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}