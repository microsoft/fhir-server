// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class AuthorizationConfiguration
    {
        public const string RolesClaim = "roles";

        public bool Enabled { get; set; }

        public RoleConfiguration RoleConfiguration { get; set; } = new RoleConfiguration();
    }
}
