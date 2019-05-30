// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public interface IModelAttributeValidator
    {
        bool TryValidate(object value, ICollection<ValidationResult> validationResults = null, bool recurse = false);
    }
}
