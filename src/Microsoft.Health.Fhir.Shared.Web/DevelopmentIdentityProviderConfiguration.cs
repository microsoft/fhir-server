﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Web
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Referenced by other assembles via shared projects.")]
    public sealed class DevelopmentIdentityProviderConfiguration
    {
        public const string Audience = "fhir-api";
        public const string LastModifiedClaim = "appid";
        public const string ClientIdClaim = "client_id";

        public bool Enabled { get; set; }

        public IList<DevelopmentIdentityProviderApplicationConfiguration> ClientApplications { get; } = new List<DevelopmentIdentityProviderApplicationConfiguration>();

        public IList<DevelopmentIdentityProviderUserConfiguration> Users { get; } = new List<DevelopmentIdentityProviderUserConfiguration>();
    }
}
