// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class USCore6InstantiateCapability : IInstantiateCapability
    {
        private static readonly string[] Urls =
        {
            "http://hl7.org/fhir/us/core/CapabilityStatement/us-core-server",
        };

        public bool TryGetUrls(out IEnumerable<string> urls)
        {
            urls = Urls;
            return true;
        }
    }
}
