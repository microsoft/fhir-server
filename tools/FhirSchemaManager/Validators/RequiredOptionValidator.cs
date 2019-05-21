// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.Linq;

namespace FhirSchemaManager.Validators
{
    public static class RequiredOptionValidator
    {
        /// <summary>
        /// Validates that the option specified is present once in the symbol
        /// </summary>
        /// <param name="symbol">The symbol representing the execution of the tool</param>
        /// <param name="requiredOption">The option that is required</param>
        /// <param name="validationErrorMessage">The message to show if the option is not present</param>
        /// <returns>A string to show the users if there is a validation error</returns>
        public static string Validate(SymbolResult symbol, Option requiredOption, string validationErrorMessage)
        {
            if (requiredOption.Aliases.Any(alias => symbol.Children.Contains(alias)))
            {
                return null;
            }

            return validationErrorMessage;
        }
    }
}
