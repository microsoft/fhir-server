// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Api.OpenIddict.Configuration;
using Microsoft.Health.Fhir.Tests.Common;
using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    /// <summary>
    /// Authentication Settings
    /// </summary>
    public static class AuthenticationSettings
    {
        public static string Scope => GetEnvironmentVariable(KnownEnvironmentVariableNames.AuthorizationScope, DevelopmentIdentityProviderConfiguration.Audience);

        public static string Resource => GetEnvironmentVariable(KnownEnvironmentVariableNames.AuthorizationResource, DevelopmentIdentityProviderConfiguration.Audience);
    }
}
