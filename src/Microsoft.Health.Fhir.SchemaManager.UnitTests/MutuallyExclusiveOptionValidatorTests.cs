// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public class MutuallyExclusiveOptionValidatorTests
{
    [Fact]
    public void GivenOneOptionPresent_WhenValidated_ThenNoErrorMessage()
    {
        // Arrange
        var option1 = new Option<string>("--option1");
        var option2 = new Option<string>("--option2");
        var option3 = new Option<string>("--option3");

        var rootCommand = new RootCommand();
        rootCommand.AddOption(option1);
        rootCommand.AddOption(option2);
        rootCommand.AddOption(option3);

        var parser = new Parser(rootCommand);
        var parseResult = parser.Parse("--option1 value1");
        var commandResult = parseResult.CommandResult;

        var mutuallyExclusiveOptions = new List<Option> { option1, option2, option3 };
        var errorMessage = "Only one option should be present";

        // Act
        MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage);

        // Assert
        Assert.Null(commandResult.ErrorMessage);
    }

    [Fact]
    public void GivenMultipleOptionsPresent_WhenValidated_ThenErrorMessageSet()
    {
        // Arrange
        var option1 = new Option<string>("--option1");
        var option2 = new Option<string>("--option2");
        var option3 = new Option<string>("--option3");

        var rootCommand = new RootCommand();
        rootCommand.AddOption(option1);
        rootCommand.AddOption(option2);
        rootCommand.AddOption(option3);

        var parser = new Parser(rootCommand);
        var parseResult = parser.Parse("--option1 value1 --option2 value2");
        var commandResult = parseResult.CommandResult;

        var mutuallyExclusiveOptions = new List<Option> { option1, option2, option3 };
        var errorMessage = "Only one option should be present";

        // Act
        MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage);

        // Assert
        Assert.Equal(errorMessage, commandResult.ErrorMessage);
    }

    [Fact]
    public void GivenNoOptionsPresent_WhenValidated_ThenErrorMessageSet()
    {
        // Arrange
        var option1 = new Option<string>("--option1");
        var option2 = new Option<string>("--option2");
        var option3 = new Option<string>("--option3");

        var rootCommand = new RootCommand();
        rootCommand.AddOption(option1);
        rootCommand.AddOption(option2);
        rootCommand.AddOption(option3);

        var parser = new Parser(rootCommand);
        var parseResult = parser.Parse(string.Empty);
        var commandResult = parseResult.CommandResult;

        var mutuallyExclusiveOptions = new List<Option> { option1, option2, option3 };
        var errorMessage = "Only one option should be present";

        // Act
        MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage);

        // Assert
        Assert.Equal(errorMessage, commandResult.ErrorMessage);
    }

    [Fact]
    public void GivenOptionWithAlias_WhenValidated_ThenDetectsCorrectly()
    {
        // Arrange
        var option1 = new Option<string>("--option1");
        option1.AddAlias("-o1");
        var option2 = new Option<string>("--option2");
        option2.AddAlias("-o2");

        var rootCommand = new RootCommand();
        rootCommand.AddOption(option1);
        rootCommand.AddOption(option2);

        var parser = new Parser(rootCommand);
        var parseResult = parser.Parse("-o1 value1");
        var commandResult = parseResult.CommandResult;

        var mutuallyExclusiveOptions = new List<Option> { option1, option2 };
        var errorMessage = "Only one option should be present";

        // Act
        MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage);

        // Assert
        Assert.Null(commandResult.ErrorMessage);
    }

    [Fact]
    public void GivenThreeOptionsAndTwoPresent_WhenValidated_ThenErrorMessageSet()
    {
        // Arrange
        var option1 = new Option<string>("--option1");
        var option2 = new Option<string>("--option2");
        var option3 = new Option<string>("--option3");

        var rootCommand = new RootCommand();
        rootCommand.AddOption(option1);
        rootCommand.AddOption(option2);
        rootCommand.AddOption(option3);

        var parser = new Parser(rootCommand);
        var parseResult = parser.Parse("--option1 value1 --option3 value3");
        var commandResult = parseResult.CommandResult;

        var mutuallyExclusiveOptions = new List<Option> { option1, option2, option3 };
        var errorMessage = "Only one option should be present";

        // Act
        MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage);

        // Assert
        Assert.Equal(errorMessage, commandResult.ErrorMessage);
    }

    [Fact]
    public void GivenNullCommandResult_WhenValidated_ThenThrowsArgumentNullException()
    {
        // Arrange
        CommandResult commandResult = null;
        var mutuallyExclusiveOptions = new List<Option> { new Option<string>("--option1") };
        var errorMessage = "Only one option should be present";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage));
    }

    [Fact]
    public void GivenNullMutuallyExclusiveOptions_WhenValidated_ThenThrowsArgumentNullException()
    {
        // Arrange
        var rootCommand = new RootCommand();
        var parser = new Parser(rootCommand);
        var parseResult = parser.Parse(string.Empty);
        var commandResult = parseResult.CommandResult;
        IReadOnlyCollection<Option> mutuallyExclusiveOptions = null;
        var errorMessage = "Only one option should be present";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage));
    }

    [Fact]
    public void GivenNullValidationErrorMessage_WhenValidated_ThenThrowsArgumentNullException()
    {
        // Arrange
        var rootCommand = new RootCommand();
        var parser = new Parser(rootCommand);
        var parseResult = parser.Parse(string.Empty);
        var commandResult = parseResult.CommandResult;
        var mutuallyExclusiveOptions = new List<Option> { new Option<string>("--option1") };
        string errorMessage = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            MutuallyExclusiveOptionValidator.Validate(commandResult, mutuallyExclusiveOptions, errorMessage));
    }
}
