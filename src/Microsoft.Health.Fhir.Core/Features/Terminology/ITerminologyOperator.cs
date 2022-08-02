// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Terminology
{
    public interface ITerminologyOperator
    {
        Parameters TryValidateCode(Resource resource, string id, string code, string system, string display);

        Parameters TryValidateCode(Parameters param);

        Parameters TryLookUp(string system, string code);

        Parameters TryLookUp(Parameters param);

        Resource TryExpand(Resource valueSet = null, FhirUri canonicalURL = null, int offset = 0, int count = 0);

        Resource TryExpand(Parameters param);
    }
}
