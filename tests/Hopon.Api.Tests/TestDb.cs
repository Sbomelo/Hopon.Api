using Hopon.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hopon.Api.Tests;

internal sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public HoponDbContext Context { get; }

    private TestDb(SqliteConnection connection)
    {
        _connection = connection;
        Context = new HoponDbContext(new DbContextOptionsBuilder<HoponDbContext>()
            .UseSqlite(connection).Options);
    }

    public static async Task<TestDb> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var database = new TestDb(connection);
        await database.Context.Database.EnsureCreatedAsync();
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
