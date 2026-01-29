// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public class CommandOptionsTests
{
    [Fact]
    public void GivenConnectionStringOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.ConnectionStringOption();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.ConnectionString);
        Assert.True(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.ConnectionStringShort);
        Assert.Equal(ArgumentArity.ExactlyOne, option.Arity);
    }

    [Fact]
    public void GivenManagedIdentityClientIdOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.ManagedIdentityClientIdOption();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.ManagedIdentityClientId);
        Assert.False(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.ManagedIdentityClientIdShort);
        Assert.Equal(ArgumentArity.ZeroOrOne, option.Arity);
    }

    [Fact]
    public void GivenAuthenticationTypeOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.AuthenticationTypeOption();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.AuthenticationType);
        Assert.False(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.AuthenticationTypeShort);
        Assert.Equal(ArgumentArity.ZeroOrOne, option.Arity);
    }

    [Fact]
    public void GivenEnableWorkloadIdentityOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.EnableWorkloadIdentityOptions();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.EnableWorkloadIdentity);
        Assert.False(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.EnableWorkloadIdentityShort);
        Assert.Equal(ArgumentArity.ZeroOrOne, option.Arity);
    }

    [Fact]
    public void GivenVersionOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.VersionOption();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.Version);
        Assert.False(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.VersionShort);
        Assert.Equal(ArgumentArity.ZeroOrOne, option.Arity);
    }

    [Fact]
    public void GivenNextOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.NextOption();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.Next);
        Assert.False(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.NextShort);
        Assert.Equal(ArgumentArity.ZeroOrOne, option.Arity);
    }

    [Fact]
    public void GivenLatestOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.LatestOption();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.Latest);
        Assert.False(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.LatestShort);
        Assert.Equal(ArgumentArity.ZeroOrOne, option.Arity);
    }

    [Fact]
    public void GivenForceOption_WhenCreated_ThenHasCorrectProperties()
    {
        // Act
        var option = CommandOptions.ForceOption();

        // Assert
        Assert.Contains(option.Aliases, a => a == OptionAliases.Force);
        Assert.False(option.IsRequired);
        Assert.Contains(option.Aliases, a => a == OptionAliases.ForceShort);
        Assert.Equal(ArgumentArity.ZeroOrOne, option.Arity);
    }

    [Fact]
    public void GivenConnectionStringOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.ConnectionStringOption();

        // Assert
        Assert.NotEmpty(option.Description);
    }

    [Fact]
    public void GivenManagedIdentityClientIdOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.ManagedIdentityClientIdOption();

        // Assert
        Assert.NotEmpty(option.Description);
    }

    [Fact]
    public void GivenAuthenticationTypeOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.AuthenticationTypeOption();

        // Assert
        Assert.NotEmpty(option.Description);
    }

    [Fact]
    public void GivenEnableWorkloadIdentityOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.EnableWorkloadIdentityOptions();

        // Assert
        Assert.NotEmpty(option.Description);
    }

    [Fact]
    public void GivenVersionOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.VersionOption();

        // Assert
        Assert.NotEmpty(option.Description);
    }

    [Fact]
    public void GivenNextOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.NextOption();

        // Assert
        Assert.NotEmpty(option.Description);
    }

    [Fact]
    public void GivenLatestOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.LatestOption();

        // Assert
        Assert.NotEmpty(option.Description);
    }

    [Fact]
    public void GivenForceOption_WhenCreated_ThenHasDescription()
    {
        // Act
        var option = CommandOptions.ForceOption();

        // Assert
        Assert.NotEmpty(option.Description);
    }
}
