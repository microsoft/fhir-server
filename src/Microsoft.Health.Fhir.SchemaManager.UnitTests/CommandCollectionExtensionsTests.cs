// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public class CommandCollectionExtensionsTests
{
    [Fact]
    public void GivenServiceCollection_WhenAddCliCommands_ThenRegistersApplyCommand()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCliCommands();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var commands = serviceProvider.GetServices<Command>();
        Assert.NotEmpty(commands);
        Assert.Contains(commands, c => c.GetType() == typeof(ApplyCommand));
    }

    [Fact]
    public void GivenServiceCollection_WhenAddCliCommands_ThenRegistersCommandsAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var schemaManagerMock = Substitute.For<ISchemaManager>();
        services.AddSingleton(schemaManagerMock);

        // Act
        services.AddCliCommands();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var command1 = serviceProvider.GetServices<Command>().FirstOrDefault(c => c is ApplyCommand);
        var command2 = serviceProvider.GetServices<Command>().FirstOrDefault(c => c is ApplyCommand);
        Assert.Same(command1, command2);
    }

    [Fact]
    public void GivenConnectionStringArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.ConnectionStringShort}", "TestConnectionString" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("TestConnectionString", config[OptionAliases.ConnectionString]);
    }

    [Fact]
    public void GivenForceArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.ForceShort}", "true" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("true", config[OptionAliases.Force]);
    }

    [Fact]
    public void GivenLatestArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.LatestShort}", "true" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("true", config[OptionAliases.Latest]);
    }

    [Fact]
    public void GivenNextArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.NextShort}", "true" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("true", config[OptionAliases.Next]);
    }

    [Fact]
    public void GivenVersionArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.VersionShort}", "5" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("5", config[OptionAliases.Version]);
    }

    [Fact]
    public void GivenManagedIdentityClientIdArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.ManagedIdentityClientIdShort}", "test-client-id" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("test-client-id", config[OptionAliases.ManagedIdentityClientId]);
    }

    [Fact]
    public void GivenAuthenticationTypeArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.AuthenticationTypeShort}", "ManagedIdentity" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("ManagedIdentity", config[OptionAliases.AuthenticationType]);
    }

    [Fact]
    public void GivenEnableWorkloadIdentityArg_WhenAddSchemaCommandLine_ThenMapsCorrectly()
    {
        // Arrange
        var args = new[] { $"{OptionAliases.EnableWorkloadIdentityShort}", "true" };
        var configBuilder = new ConfigurationBuilder();

        // Act
        configBuilder.AddSchemaCommandLine(args);
        var config = configBuilder.Build();

        // Assert
        Assert.Equal("true", config[OptionAliases.EnableWorkloadIdentity]);
    }

    [Fact]
    public void GivenConfiguration_WhenSetCommandLineOptions_ThenSetsConnectionString()
    {
        // Arrange
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string>
        {
            { OptionAliases.ConnectionString, "TestConnectionString" },
        });
        var config = configBuilder.Build();

        // Act
        services.SetCommandLineOptions(config);
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CommandLineOptions>>().Value;

        // Assert
        Assert.Equal("TestConnectionString", options.ConnectionString);
    }

    [Fact]
    public void GivenConfiguration_WhenSetCommandLineOptions_ThenSetsManagedIdentityClientId()
    {
        // Arrange
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string>
        {
            { OptionAliases.ManagedIdentityClientId, "test-client-id" },
        });
        var config = configBuilder.Build();

        // Act
        services.SetCommandLineOptions(config);
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CommandLineOptions>>().Value;

        // Assert
        Assert.Equal("test-client-id", options.ManagedIdentityClientId);
    }

#pragma warning disable CS0618 // Type or member is obsolete
    [Fact]
    public void GivenConfiguration_WhenSetCommandLineOptions_ThenSetsAuthenticationType()
    {
        // Arrange
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string>
        {
            { OptionAliases.AuthenticationType, "ManagedIdentity" },
        });
        var config = configBuilder.Build();

        // Act
        services.SetCommandLineOptions(config);
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CommandLineOptions>>().Value;

        // Assert
        Assert.Equal(SqlServerAuthenticationType.ManagedIdentity, options.AuthenticationType);
    }
#pragma warning restore CS0618 // Type or member is obsolete

    [Fact]
    public void GivenEmptyConfiguration_WhenSetCommandLineOptions_ThenDefaultsAreUsed()
    {
        // Arrange
        var services = new ServiceCollection();
        var configBuilder = new ConfigurationBuilder();
        var config = configBuilder.Build();

        // Act
        services.SetCommandLineOptions(config);
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<CommandLineOptions>>().Value;

        // Assert
        Assert.Null(options.ConnectionString);
        Assert.Null(options.AuthenticationType);
        Assert.Null(options.ManagedIdentityClientId);
    }
}
