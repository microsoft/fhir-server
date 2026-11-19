// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Ignixa.Features.FhirPackages
{
    /// <summary>
    /// Administrator-configured trusted npm sources for FHIR package acquisition.
    /// </summary>
    public sealed class FhirPackageAcquisitionOptions
    {
        /// <summary>
        /// Gets the trusted sources, keyed by source identifier.
        /// </summary>
        public IDictionary<string, FhirPackageSourceOptions> Sources { get; } = new Dictionary<string, FhirPackageSourceOptions>(StringComparer.Ordinal);
    }
}
