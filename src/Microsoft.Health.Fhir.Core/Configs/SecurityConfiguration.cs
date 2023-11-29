// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class SecurityConfiguration : ISecurityConfiguration
    {
        public delegate void AddAuthenticationLibraryMethod(IServiceCollection services, SecurityConfiguration securityConfiguration);

        public bool Enabled { get; set; }

        public bool EnableAadSmartOnFhirProxy { get; set; }

        public AuthenticationConfiguration Authentication { get; set; } = new AuthenticationConfiguration();

        public virtual HashSet<string> PrincipalClaims { get; } = new HashSet<string>(StringComparer.Ordinal);

        public AuthorizationConfiguration Authorization { get; set; } = new AuthorizationConfiguration();

        public string ServicePrincipalClientId { get; set; }

        public AddAuthenticationLibraryMethod AddAuthenticationLibrary { get; set; }
    }
}
