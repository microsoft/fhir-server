// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Web;
using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    /// <summary>
    /// Authentication Settings
    /// </summary>
    public static class AuthenticationSettings
    {
        public static string Scope => GetEnvironmentVariableWithDefault("Scope", DevelopmentIdentityProviderConfiguration.Audience);

        public static string Resource => GetEnvironmentVariableWithDefault("Resource", DevelopmentIdentityProviderConfiguration.Audience);
    }
}
