// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Manager.Model;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public class ApplyCommandTests
{
    private readonly ISchemaManager _schemaManager;
    private readonly ILogger<ApplyCommand> _logger;
    private readonly ApplyCommand _applyCommand;

    public ApplyCommandTests()
    {
        _schemaManager = Substitute.For<ISchemaManager>();
        _logger = Substitute.For<ILogger<ApplyCommand>>();
        _applyCommand = new ApplyCommand(_schemaManager, _logger);
    }

    [Fact]
    public void GivenApplyCommand_WhenCreated_ThenHasCorrectName()
    {
        // Assert
        Assert.Equal(CommandNames.Apply, _applyCommand.Name);
    }

    [Fact]
    public void GivenApplyCommand_WhenCreated_ThenHasCorrectOptions()
    {
        // Assert
        Assert.NotEmpty(_applyCommand.Options);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.ConnectionString);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.ManagedIdentityClientId);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.AuthenticationType);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.EnableWorkloadIdentity);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.Version);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.Next);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.Latest);
        Assert.Contains(_applyCommand.Options, o => o.Name == OptionAliases.Force);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public async Task GivenVersionArgument_WhenInvoked_ThenCallsSchemaManager(string option)
    {
        // Arrange
        var parseResult = _applyCommand.Parse($"--connection-string test {option} 5");

        // Act
        int exitCode = await parseResult.InvokeAsync();

        // Assert
        Assert.Equal(0, exitCode);
        await _schemaManager.Received(1).ApplySchema(
            Arg.Is<MutuallyExclusiveType>(type => type.Version == 5 && !type.Next && !type.Latest),
            false,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("--next")]
    [InlineData("-n")]
    public async Task GivenNextArgument_WhenInvoked_ThenCallsSchemaManager(string option)
    {
        // Arrange
        var parseResult = _applyCommand.Parse($"--connection-string test {option}");

        // Act
        int exitCode = await parseResult.InvokeAsync();

        // Assert
        Assert.Equal(0, exitCode);
        await _schemaManager.Received(1).ApplySchema(
            Arg.Is<MutuallyExclusiveType>(type => type.Version == 0 && type.Next && !type.Latest),
            false,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("--latest", false)]
    [InlineData("-l", false)]
    [InlineData("--latest --force", true)]
    [InlineData("-l -f", true)]
    [InlineData("--latest --force false", false)]
    public async Task GivenLatestArgument_WhenInvoked_ThenCallsSchemaManager(string options, bool force)
    {
        // Arrange
        var parseResult = _applyCommand.Parse($"--connection-string test {options}");

        // Act
        int exitCode = await parseResult.InvokeAsync();

        // Assert
        Assert.Equal(0, exitCode);
        await _schemaManager.Received(1).ApplySchema(
            Arg.Is<MutuallyExclusiveType>(type => type.Version == 0 && !type.Next && type.Latest),
            force,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenForceFlag_WhenExecute_ThenLogsInformationAndCallsSchemaManager()
    {
        // Arrange
        var type = new MutuallyExclusiveType { Latest = true };
        bool force = true;
        var cancellationToken = CancellationToken.None;

        // Act
        await _applyCommand.ExecuteAsync(type, force, cancellationToken);

        // Assert
        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString().Contains("Forcing the apply command")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception, string>>());

        await _schemaManager.Received(1).ApplySchema(type, force, cancellationToken);
    }

    [Fact]
    public async Task GivenNoForceFlag_WhenExecute_ThenDoesNotLogForceMessage()
    {
        // Arrange
        var type = new MutuallyExclusiveType { Latest = true };
        bool force = false;
        var cancellationToken = CancellationToken.None;

        // Act
        await _applyCommand.ExecuteAsync(type, force, cancellationToken);

        // Assert
        _logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(v => v.ToString().Contains("Forcing")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception, string>>());
    }

    [Fact]
    public async Task GivenCancellationRequested_WhenExecute_ThenPropagatesCancellation()
    {
        // Arrange
        var type = new MutuallyExclusiveType { Latest = true };
        bool force = false;

        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            cancellationTokenSource.Cancel();

            _schemaManager.ApplySchema(type, force, cancellationTokenSource.Token)
                .Returns(Task.FromCanceled(cancellationTokenSource.Token));

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await _applyCommand.ExecuteAsync(type, force, cancellationTokenSource.Token));
        }
    }

    [Fact]
    public void GivenNullSchemaManager_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ApplyCommand(null, _logger));
    }

    [Fact]
    public void GivenNullLogger_WhenConstructing_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ApplyCommand(_schemaManager, null));
    }
}
