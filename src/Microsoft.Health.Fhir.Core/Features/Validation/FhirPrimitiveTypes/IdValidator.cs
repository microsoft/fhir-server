// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentValidation.Resources;
using FluentValidation.Validators;

namespace Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes
{
    /// <summary>
    /// Validates a resource Id based on rules from https://www.hl7.org/fhir/datatypes.html
    /// </summary>
    /// <seealso cref="FluentValidation.Validators.RegularExpressionValidator" />
    public class IdValidator : RegularExpressionValidator
    {
        public IdValidator()
            : base("^[A-Za-z0-9\\-\\.]{1,64}$", RegexOptions.Singleline | RegexOptions.Compiled)
        {
            Options.ErrorMessageSource = new StaticStringSource(Core.Resources.IdRequirements);
        }
    }
}
