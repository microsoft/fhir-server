// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public interface ISecurityConfiguration
    {
        SecurityConfiguration.AddAuthenticationLibraryMethod AddAuthenticationLibrary { get; set; }

        AuthenticationConfiguration Authentication { get; set; }

        AuthorizationConfiguration Authorization { get; set; }

        bool EnableAadSmartOnFhirProxy { get; set; }

        bool Enabled { get; set; }

        HashSet<string> PrincipalClaims { get; }

        string ServicePrincipalClientId { get; set; }
    }
}
