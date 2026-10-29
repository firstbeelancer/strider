using FluentAssertions;
using Microsoft.Data.Sqlite;
using Strider.Core.Abstractions;
using Strider.Core.Domain;
using Strider.Infrastructure.Persistence;

namespace Strider.Infrastructure.Tests;

/// <summary>
/// AI settings persistence (ТЗ F5.1–F5.3) and the "API key is a keychain
/// reference, never a value in the database" rule (F-008).
/// </summary>
public class SqliteAiSettingsStoreTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteAiSettingsStore _store;

    public SqliteAiSettingsStoreTests()
    {
        var connStr = $"Data Source=file:ai{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection(connStr);
        _keepAlive.Open();

        new DatabaseInitializer(connStr).InitializeAsync().GetAwaiter().GetResult();
        _store = new SqliteAiSettingsStore(connStr);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task GetDefault_OnEmptyDatabase_ReturnsNull()
    {
        (await _store.GetDefaultAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Save_ThenRead_RoundTripsAllFields()
    {
        var settings = new AiSettings
        {
            Provider = "openrouter",
            Model = "gpt-4o-mini",
            BaseUrl = "https://openrouter.ai/api/v1",
            ApiKeyRef = KeychainKeys.AiApiKey(Guid.NewGuid()),
            IsDefault = true,
        };

        await _store.SaveAsync(settings);
        var loaded = await _store.GetDefaultAsync();

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(settings.Id);
        loaded.Provider.Should().Be("openrouter");
        loaded.Model.Should().Be("gpt-4o-mini");
        loaded.BaseUrl.Should().Be("https://openrouter.ai/api/v1");
        loaded.ApiKeyRef.Should().Be(settings.ApiKeyRef);
        loaded.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Save_Twice_UpdatesInsteadOfDuplicating()
    {
        var settings = new AiSettings { Provider = "openai", Model = "gpt-4o-mini", IsDefault = true };
        await _store.SaveAsync(settings);

        settings.Model = "gpt-4o";
        await _store.SaveAsync(settings);

        var all = await _store.GetAllAsync();
        all.Should().ContainSingle();
        all[0].Model.Should().Be("gpt-4o");
    }

    [Fact]
    public async Task Save_SecondDefault_ClearsThePreviousOne()
    {
        var first = new AiSettings { Provider = "openai", Model = "gpt-4o-mini", IsDefault = true };
        await _store.SaveAsync(first);

        var second = new AiSettings { Provider = "anthropic", Model = "claude-haiku", IsDefault = true };
        await _store.SaveAsync(second);

        var all = await _store.GetAllAsync();
        all.Should().HaveCount(2);
        all.Count(s => s.IsDefault).Should().Be(1);
        (await _store.GetDefaultAsync())!.Provider.Should().Be("anthropic");
    }

    [Fact]
    public async Task GetDefault_FallsBackToNewest_WhenNobodyIsMarkedDefault()
    {
        await _store.SaveAsync(new AiSettings { Provider = "openai", Model = "gpt-4o-mini" });

        (await _store.GetDefaultAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheRow()
    {
        var settings = new AiSettings { Provider = "openai", Model = "gpt-4o-mini", IsDefault = true };
        await _store.SaveAsync(settings);

        await _store.DeleteAsync(settings.Id);

        (await _store.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApiKeyRef_IsStoredNotTheSecret()
    {
        // F-008 regression guard: the DB column holds a keychain key, never a secret.
        var id = Guid.NewGuid();
        await _store.SaveAsync(new AiSettings
        {
            Id = id,
            Provider = "openai",
            Model = "gpt-4o-mini",
            ApiKeyRef = KeychainKeys.AiApiKey(id),
            IsDefault = true,
        });

        var loaded = await _store.GetDefaultAsync();

        loaded!.ApiKeyRef.Should().Be($"strider:ai:{id}:api_key");
        loaded.ApiKeyRef.Should().NotContain("sk-");
    }
}
