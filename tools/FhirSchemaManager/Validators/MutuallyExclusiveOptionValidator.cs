// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

namespace FhirSchemaManager.Validators
{
    public static class MutuallyExclusiveOptionValidator
    {
        /// <summary>
        /// Validates that only one of the option from given options is present in the symbol
        /// </summary>
        /// <param name="symbol">The symbol representing the execution of the tool</param>
        /// <param name="mutuallyExclusiveOptions">The option that is required</param>
        /// <param name="validationErrorMessage">The message to show if only one of the option is not present</param>
        /// <returns>A string to show the users if there is a validation error</returns>
        public static string Validate(SymbolResult symbol, List<Option> mutuallyExclusiveOptions, string validationErrorMessage)
        {
            int count = 0;

            foreach (Option mutuallyExclusiveOption in mutuallyExclusiveOptions)
            {
                if (mutuallyExclusiveOption.Aliases.Any(alias => symbol.Children.Contains(alias)))
                {
                    count++;
                }
            }

            return (count != 1) ? validationErrorMessage : null;
        }
    }
}
