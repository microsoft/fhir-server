// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestApplication
    {
        public TestApplication(string id)
        {
            Id = id;
        }

        public string Id { get; }

        public string ClientId => GetEnvironmentVariableWithDefault($"app_{Id}_id", Id);

        public string ClientSecret => GetEnvironmentVariableWithDefault($"app_{Id}_secret", Id);

        public string GrantType => GetEnvironmentVariableWithDefault($"app_{Id}_grant_type", "client_credentials");
    }
}