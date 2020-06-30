// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Api.Features.Throttling;

namespace Microsoft.Health.Fhir.Api.Configs
{
    public class ThrottlingConfiguration
    {
        public bool Enabled { get; set; }

        public int ConcurrentRequestLimit { get; set; }

        public HashSet<ExcludedEndpoint> ExcludedEndpoints { get; } = new HashSet<ExcludedEndpoint>();
    }
}
