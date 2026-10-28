using GreenHedges.HomeServer.PayAndPensions.Data;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>
/// Base for tests that use the database. Each test gets its own test user (<see cref="UserId"/>),
/// plus any made with <see cref="NewUser"/>, and all their data is deleted after the test.
/// </summary>
public abstract class DatabaseTest : IAsyncLifetime
{
    private readonly List<string> _users = [];

    protected DatabaseTest() => UserId = NewUser();

    protected string UserId { get; }

    protected string NewUser()
    {
        var id = TestDatabase.NewUserId();
        _users.Add(id);
        return id;
    }

    /// <summary>A new context acting as the test's user.</summary>
    protected AppDbContext Db() => TestDatabase.As(UserId);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var user in _users) await TestDatabase.DeleteUserDataAsync(user);
    }
}
