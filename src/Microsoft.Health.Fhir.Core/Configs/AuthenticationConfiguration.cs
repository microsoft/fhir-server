// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class AuthenticationConfiguration
    {
        public string Audience { get; set; }

        public string Authority { get; set; }

        public string IntrospectionEndpoint { get; set; }

        public string ManagementEndpoint { get; set; }

        public string RevocationEndpoint { get; set; }
    }
}
