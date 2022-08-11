// -------------------------------------------------------------------------------------------------
// <copyright file="MutuallyExclusiveOptionValidator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SchemaManager;

using System.CommandLine;
using System.CommandLine.Parsing;
using EnsureThat;

public static class MutuallyExclusiveOptionValidator
{
    /// <summary>
    /// Validates that only one of the option from given options is present in the symbol
    /// </summary>
    /// <param name="commandResult">The result of the command being run</param>
    /// <param name="mutuallyExclusiveOptions">The list of mutually exclusive options</param>
    /// <param name="validationErrorMessage">The message to show if only one of the option is not present</param>
    /// <returns>A string to show the users if there is a validation error</returns>
    public static void Validate(CommandResult commandResult, IEnumerable<Option> mutuallyExclusiveOptions, string validationErrorMessage)
    {
        EnsureArg.IsNotNull(commandResult, nameof(commandResult));
        EnsureArg.IsNotNull(mutuallyExclusiveOptions, nameof(mutuallyExclusiveOptions));
        EnsureArg.IsNotNull(validationErrorMessage, nameof(validationErrorMessage));

        int count = 0;

        foreach (Option mutuallyExclusiveOption in mutuallyExclusiveOptions)
        {
            if (commandResult.ContainsAnyAliasOf(mutuallyExclusiveOption))
            {
                count++;
            }
        }

        if (count != 1)
        {
            commandResult.ErrorMessage = validationErrorMessage;
        }
    }

    private static bool ContainsAnyAliasOf(this CommandResult commandResult, Option option)
    {
        foreach (string? alias in option.Aliases)
        {
            if (commandResult.Children.Any(sr => sr.Symbol is IdentifierSymbol id && id.HasAlias(alias)))
            {
                return true;
            }
        }

        return false;
    }
}
