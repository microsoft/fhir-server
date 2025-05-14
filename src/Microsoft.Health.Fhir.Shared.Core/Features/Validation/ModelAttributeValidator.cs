// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Hl7.Fhir.Validation;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ModelAttributeValidator : IModelAttributeValidator
    {
        public bool TryValidate(ResourceElement value, ICollection<ValidationResult> validationResults = null, bool recurse = false)
        {
            var results = value.ToPoco().Validate();

            if (results.Count > 0 && validationResults != null)
            {
                foreach (var item in results)
                {
                    validationResults.Add(new ValidationResult(item.Message));
                }
            }

            return results.Count == 0;
        }
    }
}
