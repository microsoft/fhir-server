// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public static class AuthenticationSettings
    {
        public static string ClientId => GetEnvironmentVariableWithDefault("ClientId", "known-client-id");

        public static string ClientSecret => GetEnvironmentVariableWithDefault("ClientSecret", "known-client-secret");

        public static string GrantType => GetEnvironmentVariableWithDefault("GrantType", "password");

        public static string Scope => GetEnvironmentVariableWithDefault("Scope", "fhir-api");

        public static string Resource => GetEnvironmentVariableWithDefault("Resource", string.Empty);

        private static string GetEnvironmentVariableWithDefault(string environmentVariableName, string defaultValue)
        {
            var evironmentVariable = Environment.GetEnvironmentVariable(environmentVariableName);

            return string.IsNullOrWhiteSpace(evironmentVariable) ? defaultValue : evironmentVariable;
        }
    }
}
