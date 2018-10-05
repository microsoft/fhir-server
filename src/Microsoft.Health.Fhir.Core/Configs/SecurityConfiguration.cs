// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class SecurityConfiguration
    {
        public bool Enabled { get; set; }

        public AuthenticationConfiguration Authentication { get; } = new AuthenticationConfiguration();

        public virtual HashSet<string> LastModifiedClaims { get; } = new HashSet<string>();

        public AuthorizationConfiguration Authorization { get; } = new AuthorizationConfiguration();
    }
}
