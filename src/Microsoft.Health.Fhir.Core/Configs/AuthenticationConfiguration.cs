// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class AuthenticationConfiguration
    {
        public AuthenticationMode Mode { get; set; }

        public string Audience { get; set; }

        public string Authority { get; set; }
    }
}
