using Microsoft.Extensions.Configuration;
using OpenTaan.GameServer.Accounts;

namespace OpenTaan.Tests.GameServer;

/// <summary>The account settings: what the server ships with, and what it refuses to start with.</summary>
public sealed class AccountSettingsTests
{
    [Fact]
    public void TheShippedSettingsHashPasswordsWith600000Rounds()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .Build()
            .GetSection(AccountOptions.Section)
            .Get<AccountOptions>()!;

        Assert.Equal(600_000, shipped.PasswordIterations);
        Assert.True(new AccountOptionsValidation().Validate(null, shipped).Succeeded);
    }

    [Fact]
    public void FewerRoundsAreRefusedNamingTheSetting()
    {
        var result = new AccountOptionsValidation().Validate(null, new AccountOptions { PasswordIterations = 599_999 });

        Assert.True(result.Failed);
        Assert.Contains("Accounts:PasswordIterations", result.FailureMessage, StringComparison.Ordinal);
    }
}
