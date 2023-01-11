// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Parsing;
using EnsureThat;

namespace Microsoft.Health.Fhir.SchemaManager;

public static class MutuallyExclusiveOptionValidator
{
    /// <summary>
    /// Validates that only one of the option from given options is present in the symbol
    /// </summary>
    /// <param name="commandResult">The result of the command being run</param>
    /// <param name="mutuallyExclusiveOptions">The list of mutually exclusive options</param>
    /// <param name="validationErrorMessage">The message to show if only one of the option is not present</param>
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
