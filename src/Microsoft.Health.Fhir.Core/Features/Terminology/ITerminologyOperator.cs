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

        Parameters TryValidateCode(Resource parameters);
    }
}
