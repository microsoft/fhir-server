// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

        /// <summary>
        /// Gets the token endpoint used by remote E2E client-credential authentication.
        /// </summary>
        public static Uri TestTokenEndpoint => ParseTestTokenEndpoint(GetEnvironmentVariable(KnownEnvironmentVariableNames.TestTokenEndpoint));

        internal static Uri ParseTestTokenEndpoint(string tokenEndpoint)
        {
            if (string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                throw new InvalidOperationException(
                    $"{KnownEnvironmentVariableNames.TestTokenEndpoint} must be configured for remote E2E tests.");
            }

            if (!Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out Uri endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"{KnownEnvironmentVariableNames.TestTokenEndpoint} must be an absolute HTTP or HTTPS URI. " +
                    $"Received '{tokenEndpoint}'.");
            }

            return endpoint;
        }
    }
}
