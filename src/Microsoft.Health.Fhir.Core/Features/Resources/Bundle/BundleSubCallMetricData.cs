// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Resources.Bundle
{
    public class BundleSubCallMetricData
    {
        /// <summary>
        /// The type of FHIR resource associated with this context.
        /// </summary>
        public string ResourceType { get; set; }

        /// <summary>
        /// The FHIR operation being performed.
        /// </summary>
        public string FhirOperation { get; set; }
    }
}
