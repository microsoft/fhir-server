// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task GivenVersionType_WhenExecute_ThenCallsSchemaManager()
    {
        // Arrange
        var type = new MutuallyExclusiveType { Version = 5 };
        bool force = false;
        var cancellationToken = CancellationToken.None;

        // Act
        await _applyCommand.ExecuteAsync(type, force, cancellationToken);

        // Assert
        await _schemaManager.Received(1).ApplySchema(type, force, cancellationToken);
    }

    [Fact]
    public async Task GivenNextType_WhenExecute_ThenCallsSchemaManager()
    {
        // Arrange
        var type = new MutuallyExclusiveType { Next = true };
        bool force = false;
        var cancellationToken = CancellationToken.None;

        // Act
        await _applyCommand.ExecuteAsync(type, force, cancellationToken);

        // Assert
        await _schemaManager.Received(1).ApplySchema(type, force, cancellationToken);
    }

    [Fact]
    public async Task GivenLatestType_WhenExecute_ThenCallsSchemaManager()
    {
        // Arrange
        var type = new MutuallyExclusiveType { Latest = true };
        bool force = false;
        var cancellationToken = CancellationToken.None;

        // Act
        await _applyCommand.ExecuteAsync(type, force, cancellationToken);

        // Assert
        await _schemaManager.Received(1).ApplySchema(type, force, cancellationToken);
    }

    [Theory]
    [InlineData("--version 5", 5, false, false, false)]
    [InlineData("-v 5", 5, false, false, false)]
    [InlineData("--next", 0, true, false, false)]
    [InlineData("-n", 0, true, false, false)]
    [InlineData("--latest", 0, false, true, false)]
    [InlineData("-l", 0, false, true, false)]
    [InlineData("--latest --force", 0, false, true, true)]
    [InlineData("-l -f", 0, false, true, true)]
    [InlineData("--latest --force false", 0, false, true, false)]
    public async Task GivenValidArguments_WhenInvoked_ThenBindsSchemaRequest(string options, int version, bool next, bool latest, bool force)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Command>(_applyCommand);
        using var serviceProvider = services.BuildServiceProvider();
        var rootCommand = SchemaManagerParserBuilder.Build(serviceProvider);
        var parseResult = rootCommand.Parse($"apply --connection-string \"Server=localhost;Database=fhir\" {options}");

        // Act
        int exitCode = await parseResult.InvokeAsync();

        // Assert
        Assert.Empty(parseResult.Errors);
        Assert.Equal(0, exitCode);
        await _schemaManager.Received(1).ApplySchema(
            Arg.Is<MutuallyExclusiveType>(type => type.Version == version && type.Next == next && type.Latest == latest),
            force,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("apply --connection-string test")]
    [InlineData("apply --connection-string test --version 5 --next")]
    [InlineData("apply --connection-string test --next --latest")]
    [InlineData("apply --connection-string test --version 5 --latest")]
    [InlineData("apply --connection-string test --version invalid")]
    [InlineData("apply --latest")]
    [InlineData("apply --connection-string test --latest --unknown")]
    public async Task GivenInvalidArguments_WhenInvoked_ThenRejectsWithoutApplyingSchema(string arguments)
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(_applyCommand);
        var parseResult = rootCommand.Parse(arguments);

        // Act
        int exitCode = await parseResult.InvokeAsync();

        // Assert
        Assert.NotEmpty(parseResult.Errors);
        Assert.NotEqual(0, exitCode);
        await _schemaManager.DidNotReceiveWithAnyArgs().ApplySchema(default, default, default);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("apply --help")]
    public async Task GivenHelpArgument_WhenInvoked_ThenSucceedsWithoutApplyingSchema(string arguments)
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Subcommands.Add(_applyCommand);
        var parseResult = rootCommand.Parse(arguments);

        // Act
        int exitCode = await parseResult.InvokeAsync();

        // Assert
        Assert.Equal(0, exitCode);
        await _schemaManager.DidNotReceiveWithAnyArgs().ApplySchema(default, default, default);
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
