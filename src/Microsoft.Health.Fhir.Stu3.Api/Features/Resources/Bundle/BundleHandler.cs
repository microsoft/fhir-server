// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// FHIR STU3 specific portions of the bundle handler.
    /// </summary>
    public partial class BundleHandler
    {
        private BundleHandler()
        {
            _verbExecutionOrder = new[]
            {
                Hl7.Fhir.Model.Bundle.HTTPVerb.DELETE,
                Hl7.Fhir.Model.Bundle.HTTPVerb.POST,
                Hl7.Fhir.Model.Bundle.HTTPVerb.PUT,
                Hl7.Fhir.Model.Bundle.HTTPVerb.GET,
            };
        }
    }
}
