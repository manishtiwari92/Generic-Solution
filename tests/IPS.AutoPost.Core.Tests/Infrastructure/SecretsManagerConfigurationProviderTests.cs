using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FluentAssertions;
using IPS.AutoPost.Core.Infrastructure;
using Microsoft.Extensions.Configuration;
using Moq;

namespace IPS.AutoPost.Core.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="SecretsManagerConfigurationProvider"/>.
/// Verifies that "/" prefixed config values are replaced with fetched secrets,
/// non-"/" values are left unchanged, JSON secrets with AppConnectionString are
/// correctly extracted, and that timeout and missing-secret errors are surfaced
/// as <see cref="InvalidOperationException"/>.
/// </summary>
public class SecretsManagerConfigurationProviderTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds an <see cref="IConfigurationBuilder"/> pre-seeded with the given
    /// in-memory key/value pairs.
    /// </summary>
    private static IConfigurationBuilder BuilderWith(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values);

    /// <summary>
    /// Creates a mock <see cref="IAmazonSecretsManager"/> that returns
    /// <paramref name="secretValue"/> for <paramref name="secretId"/>.
    /// </summary>
    private static Mock<IAmazonSecretsManager> MockSecretsManager(
        string secretId,
        string secretValue)
    {
        var mock = new Mock<IAmazonSecretsManager>();
        mock.Setup(sm => sm.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(r => r.SecretId == secretId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = secretValue });
        return mock;
    }

    /// <summary>
    /// Creates a mock that returns the given secret value for ANY secret ID.
    /// </summary>
    private static Mock<IAmazonSecretsManager> MockSecretsManagerAny(string secretValue)
    {
        var mock = new Mock<IAmazonSecretsManager>();
        mock.Setup(sm => sm.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = secretValue });
        return mock;
    }

    // -----------------------------------------------------------------------
    // 1. ConnectionStrings "/" value is replaced with fetched secret
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSecretsManagerAsync_ConnectionString_SlashValue_IsReplacedWithFetchedSecret()
    {
        // Arrange
        const string secretPath = "/IPS/Common/prod/Database/Workflow";
        const string resolvedValue = "Server=rds.example.com;Database=Workflow;User ID=sa;Password=secret;";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = secretPath
        });

        var mockSm = MockSecretsManager(secretPath, resolvedValue);

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert
        config.GetConnectionString("Workflow").Should().Be(resolvedValue,
            because: "the '/' prefixed connection string must be replaced with the fetched secret value");
    }

    [Fact]
    public async Task AddSecretsManagerAsync_MultipleConnectionStrings_AllAreReplaced()
    {
        // Arrange
        const string path1 = "/IPS/Common/prod/Database/Workflow";
        const string path2 = "/IPS/Common/prod/Database/Reporting";
        const string resolved1 = "Server=rds1.example.com;Database=Workflow;";
        const string resolved2 = "Server=rds2.example.com;Database=Reporting;";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = path1,
            ["ConnectionStrings:Reporting"] = path2
        });

        var mock = new Mock<IAmazonSecretsManager>();
        mock.Setup(sm => sm.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(r => r.SecretId == path1),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = resolved1 });
        mock.Setup(sm => sm.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(r => r.SecretId == path2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = resolved2 });

        // Act
        await builder.AddSecretsManagerAsync(mock.Object);
        var config = builder.Build();

        // Assert
        config.GetConnectionString("Workflow").Should().Be(resolved1);
        config.GetConnectionString("Reporting").Should().Be(resolved2);
    }

    // -----------------------------------------------------------------------
    // 2. Email:SmtpPassword "/" value is replaced
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSecretsManagerAsync_EmailSmtpPassword_SlashValue_IsReplaced()
    {
        // Arrange
        const string secretPath = "/IPS/Common/prod/Smtp";
        const string resolvedPassword = "smtp-secret-password-123";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["Email:SmtpPassword"] = secretPath
        });

        var mockSm = MockSecretsManager(secretPath, resolvedPassword);

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert
        config["Email:SmtpPassword"].Should().Be(resolvedPassword,
            because: "Email:SmtpPassword with a '/' prefix must be resolved from Secrets Manager");
    }

    // -----------------------------------------------------------------------
    // 3. ApiKey:Value "/" value is replaced
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSecretsManagerAsync_ApiKeyValue_SlashValue_IsReplaced()
    {
        // Arrange
        const string secretPath = "/IPS/Common/prod/ApiKey";
        const string resolvedApiKey = "my-super-secret-api-key-xyz";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ApiKey:Value"] = secretPath
        });

        var mockSm = MockSecretsManager(secretPath, resolvedApiKey);

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert
        config["ApiKey:Value"].Should().Be(resolvedApiKey,
            because: "ApiKey:Value with a '/' prefix must be resolved from Secrets Manager");
    }

    // -----------------------------------------------------------------------
    // 4. Non-"/" values are unchanged
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSecretsManagerAsync_NonSlashConnectionString_IsNotFetched()
    {
        // Arrange — plain connection string (no "/" prefix)
        const string plainConnectionString = "Server=localhost;Database=Workflow;Trusted_Connection=True;";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = plainConnectionString
        });

        var mockSm = new Mock<IAmazonSecretsManager>();

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert — value unchanged, Secrets Manager never called
        config.GetConnectionString("Workflow").Should().Be(plainConnectionString,
            because: "non-'/' prefixed values must not be fetched from Secrets Manager");

        mockSm.Verify(
            sm => sm.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddSecretsManagerAsync_NonSlashEmailPassword_IsNotFetched()
    {
        // Arrange — plain SMTP password (no "/" prefix)
        const string plainPassword = "plain-smtp-password";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["Email:SmtpPassword"] = plainPassword
        });

        var mockSm = new Mock<IAmazonSecretsManager>();

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert
        config["Email:SmtpPassword"].Should().Be(plainPassword);
        mockSm.Verify(
            sm => sm.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddSecretsManagerAsync_EmptyConfig_DoesNotCallSecretsManager()
    {
        // Arrange — no ConnectionStrings, Email, or ApiKey sections at all
        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["SomeOtherKey"] = "some-value"
        });

        var mockSm = new Mock<IAmazonSecretsManager>();

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);

        // Assert — Secrets Manager never called
        mockSm.Verify(
            sm => sm.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // 5. JSON secret with AppConnectionString key is correctly extracted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSecretsManagerAsync_JsonSecretWithAppConnectionString_ExtractsCorrectValue()
    {
        // Arrange — RDS-managed secret returns a JSON object
        const string secretPath = "/IPS/Common/prod/Database/Workflow";
        const string expectedConnectionString = "Server=rds.example.com;Database=Workflow;User ID=sa;Password=rds-managed-pw;";
        var jsonSecret = $$"""{"AppConnectionString":"{{expectedConnectionString}}","engine":"sqlserver","host":"rds.example.com"}""";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = secretPath
        });

        var mockSm = MockSecretsManager(secretPath, jsonSecret);

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert — AppConnectionString property extracted from JSON
        config.GetConnectionString("Workflow").Should().Be(expectedConnectionString,
            because: "when the secret is a JSON object with AppConnectionString, that value must be extracted");
    }

    [Fact]
    public async Task AddSecretsManagerAsync_JsonSecretWithoutAppConnectionString_UsesRawJson()
    {
        // Arrange — JSON secret without AppConnectionString key
        const string secretPath = "/IPS/Common/prod/ApiKey";
        const string jsonSecret = """{"key":"my-api-key","version":"1"}""";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ApiKey:Value"] = secretPath
        });

        var mockSm = MockSecretsManager(secretPath, jsonSecret);

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert — raw JSON returned when no AppConnectionString key
        config["ApiKey:Value"].Should().Be(jsonSecret,
            because: "when the JSON secret has no AppConnectionString key, the raw JSON string must be used");
    }

    [Fact]
    public async Task AddSecretsManagerAsync_PlainStringSecret_UsedAsIs()
    {
        // Arrange — plain string secret (not JSON)
        const string secretPath = "/IPS/Common/prod/Smtp";
        const string plainSecret = "smtp-password-plain";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["Email:SmtpPassword"] = secretPath
        });

        var mockSm = MockSecretsManager(secretPath, plainSecret);

        // Act
        await builder.AddSecretsManagerAsync(mockSm.Object);
        var config = builder.Build();

        // Assert
        config["Email:SmtpPassword"].Should().Be(plainSecret,
            because: "a plain string secret must be used as-is without JSON parsing");
    }

    // -----------------------------------------------------------------------
    // 6. Timeout throws InvalidOperationException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSecretsManagerAsync_Timeout_ThrowsInvalidOperationException()
    {
        // Arrange — mock that never completes (simulates timeout)
        const string secretPath = "/IPS/Common/prod/Database/Workflow";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = secretPath
        });

        var mockSm = new Mock<IAmazonSecretsManager>();
        mockSm.Setup(sm => sm.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<GetSecretValueRequest, CancellationToken>(async (_, ct) =>
            {
                // Delay indefinitely until the cancellation token fires
                await Task.Delay(Timeout.Infinite, ct);
                return new GetSecretValueResponse();
            });

        // Act — use a very short timeout to trigger the timeout path quickly
        var act = async () => await builder.AddSecretsManagerAsync(
            mockSm.Object,
            timeout: TimeSpan.FromMilliseconds(100));

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a timeout must surface as InvalidOperationException")
            .WithMessage("*timed out*",
                because: "the error message must indicate a timeout occurred");
    }

    [Fact]
    public async Task AddSecretsManagerAsync_Timeout_MessageContainsTimeoutDuration()
    {
        // Arrange
        const string secretPath = "/IPS/Common/prod/Database/Workflow";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = secretPath
        });

        var mockSm = new Mock<IAmazonSecretsManager>();
        mockSm.Setup(sm => sm.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<GetSecretValueRequest, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return new GetSecretValueResponse();
            });

        // Act — use the default 30-second timeout expressed as a short value for test speed
        var act = async () => await builder.AddSecretsManagerAsync(
            mockSm.Object,
            timeout: TimeSpan.FromMilliseconds(50));

        // Assert — message must mention the timeout duration in seconds
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().MatchRegex(@"\d+s",
            because: "the timeout error message must include the duration in seconds");
    }

    // -----------------------------------------------------------------------
    // 7. Missing secret throws InvalidOperationException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddSecretsManagerAsync_SecretNotFound_ThrowsInvalidOperationException()
    {
        // Arrange — mock throws ResourceNotFoundException (secret does not exist)
        const string secretPath = "/IPS/Common/prod/Database/NonExistent";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = secretPath
        });

        var mockSm = new Mock<IAmazonSecretsManager>();
        mockSm.Setup(sm => sm.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Secret not found"));

        // Act
        var act = async () => await builder.AddSecretsManagerAsync(mockSm.Object);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a missing secret must surface as InvalidOperationException")
            .WithMessage($"*'{secretPath}'*not found*",
                because: "the error message must include the secret path and indicate it was not found");
    }

    [Fact]
    public async Task AddSecretsManagerAsync_SecretNotFound_MessageContainsSecretPath()
    {
        // Arrange
        const string secretPath = "/IPS/InvitedClub/prod/PostAuth";

        var builder = BuilderWith(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workflow"] = secretPath
        });

        var mockSm = new Mock<IAmazonSecretsManager>();
        mockSm.Setup(sm => sm.GetSecretValueAsync(
                It.IsAny<GetSecretValueRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("Secret not found"));

        // Act
        var act = async () => await builder.AddSecretsManagerAsync(mockSm.Object);

        // Assert — error message must contain the secret path
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain(secretPath,
            because: "the error message must identify which secret was not found");
    }

    // -----------------------------------------------------------------------
    // 8. ExtractSecretValue — internal helper unit tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void ExtractSecretValue_NullOrWhitespace_ReturnsInput(string? input, string? expected)
    {
        // Act
        var result = SecretsManagerConfigurationProvider.ExtractSecretValue(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ExtractSecretValue_PlainString_ReturnsAsIs()
    {
        // Arrange
        const string plain = "Server=localhost;Database=Test;";

        // Act
        var result = SecretsManagerConfigurationProvider.ExtractSecretValue(plain);

        // Assert
        result.Should().Be(plain);
    }

    [Fact]
    public void ExtractSecretValue_JsonWithAppConnectionString_ExtractsValue()
    {
        // Arrange
        const string expected = "Server=rds.example.com;Database=Workflow;";
        var json = $$"""{"AppConnectionString":"{{expected}}","engine":"sqlserver"}""";

        // Act
        var result = SecretsManagerConfigurationProvider.ExtractSecretValue(json);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ExtractSecretValue_JsonWithoutAppConnectionString_ReturnsRawJson()
    {
        // Arrange
        const string json = """{"username":"admin","password":"secret"}""";

        // Act
        var result = SecretsManagerConfigurationProvider.ExtractSecretValue(json);

        // Assert
        result.Should().Be(json,
            because: "JSON without AppConnectionString must be returned as-is");
    }

    [Fact]
    public void ExtractSecretValue_InvalidJson_ReturnsRawString()
    {
        // Arrange — looks like JSON but is malformed
        const string malformed = "{not valid json}";

        // Act
        var result = SecretsManagerConfigurationProvider.ExtractSecretValue(malformed);

        // Assert — must not throw, returns raw string
        result.Should().Be(malformed,
            because: "malformed JSON must be returned as-is without throwing");
    }

    // -----------------------------------------------------------------------
    // 9. CollectSecretPaths — internal helper unit tests
    // -----------------------------------------------------------------------

    [Fact]
    public void CollectSecretPaths_OnlyReturnsSlashPrefixedValues()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Workflow"] = "/IPS/Common/prod/Database/Workflow",
                ["ConnectionStrings:Local"] = "Server=localhost;Database=Test;",  // no slash
                ["Email:SmtpPassword"] = "/IPS/Common/prod/Smtp",
                ["ApiKey:Value"] = "/IPS/Common/prod/ApiKey",
                ["SomeOtherKey"] = "plain-value"
            })
            .Build();

        // Act
        var paths = SecretsManagerConfigurationProvider.CollectSecretPaths(config);

        // Assert — only the three "/" prefixed values
        paths.Should().HaveCount(3,
            because: "only '/' prefixed values in the scanned sections should be collected");
        paths.Should().ContainKey("ConnectionStrings:Workflow");
        paths.Should().ContainKey("Email:SmtpPassword");
        paths.Should().ContainKey("ApiKey:Value");
        paths.Should().NotContainKey("ConnectionStrings:Local",
            because: "non-'/' prefixed connection strings must not be collected");
        paths.Should().NotContainKey("SomeOtherKey",
            because: "keys outside the scanned sections must not be collected");
    }

    // -----------------------------------------------------------------------
    // 10. ResolveRegion — environment variable priority
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveRegion_WhenAwsDefaultRegionSet_ReturnsIt()
    {
        // Arrange
        var original = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        try
        {
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", "eu-west-1");
            Environment.SetEnvironmentVariable("AWS_REGION", "us-west-2");

            // Act
            var region = SecretsManagerConfigurationProvider.ResolveRegion();

            // Assert
            region.Should().Be("eu-west-1",
                because: "AWS_DEFAULT_REGION takes priority over AWS_REGION");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", original);
        }
    }

    [Fact]
    public void ResolveRegion_WhenOnlyAwsRegionSet_ReturnsIt()
    {
        // Arrange
        var originalDefault = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        var originalRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        try
        {
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", null);
            Environment.SetEnvironmentVariable("AWS_REGION", "ap-southeast-1");

            // Act
            var region = SecretsManagerConfigurationProvider.ResolveRegion();

            // Assert
            region.Should().Be("ap-southeast-1",
                because: "AWS_REGION is used when AWS_DEFAULT_REGION is not set");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", originalDefault);
            Environment.SetEnvironmentVariable("AWS_REGION", originalRegion);
        }
    }

    [Fact]
    public void ResolveRegion_WhenNeitherEnvVarSet_FallsBackToUsEast1()
    {
        // Arrange
        var originalDefault = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION");
        var originalRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        try
        {
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", null);
            Environment.SetEnvironmentVariable("AWS_REGION", null);

            // Act
            var region = SecretsManagerConfigurationProvider.ResolveRegion();

            // Assert
            region.Should().Be("us-east-1",
                because: "when neither AWS_DEFAULT_REGION nor AWS_REGION is set, the fallback must be 'us-east-1'");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", originalDefault);
            Environment.SetEnvironmentVariable("AWS_REGION", originalRegion);
        }
    }
}
