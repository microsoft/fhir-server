// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Api.Configs
{
    public class ThrottlingConfiguration
    {
        public int ConcurrentRequestLimit { get; set; }

        public HashSet<string> ExcludedEndpoints { get; } = new HashSet<string>();
    }
}
