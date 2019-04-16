// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;

namespace FhirSchemaManager.Validators
{
    public static class Required
    {
        public static string Validate(SymbolResult symbol, Option requiredOption, string validationMessage)
        {
            var valid = false;

            foreach (string alias in requiredOption.Aliases)
            {
                if (symbol.Children.Contains(alias))
                {
                    valid = true;
                }
            }

            return !valid ? validationMessage : null;
        }
    }
}
