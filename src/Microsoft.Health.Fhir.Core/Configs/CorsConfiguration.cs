// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Cors;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class CorsConfiguration
    {
        public CorsMode Mode { get; set; }

        public IList<string> AllowedOrigins { get; set; } = new List<string>();
    }
}
