// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public interface IProfileValidator
    {
        /// <summary>
        /// Validate resource to profile and return collection of issues.
        /// </summary>
        /// <param name="resource">Resource to validate.</param>
        /// <param name="profile">Profile url to check. If <see langword="null"/>> we will validate according to meta profiles in resource.</param>
        OperationOutcomeIssue[] TryValidate(ITypedElement resource, string profile = null);

        Parameters TryValidateCodeValueSet(Resource valueset, string system, string id, string code, string display);
    }
}
