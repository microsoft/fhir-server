// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public interface IProfileValidator
    {
        /// <summary>
        /// Validate element to profile, and throw <see cref="ProfileValidationFailedException"/> if element is not valid.
        /// </summary>
        /// <param name="element">Element to validate.</param>
        /// <param name="profile">Profile url to check. If <see langword="null"/>> we will validate according to meta profiles in element.</param>
        OperationOutcomeIssue[] TryValidate(ITypedElement element, string profile = null);
    }
}
