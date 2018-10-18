// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestApplication
    {
        private readonly string _id;

        public TestApplication(string id)
        {
            _id = id;
        }

        public string ClientId => GetEnvironmentVariableWithDefault($"app_{_id}_id", _id);

        public string ClientSecret => GetEnvironmentVariableWithDefault($"app_{_id}_secret", _id);

        public string GrantType => GetEnvironmentVariableWithDefault($"app_{_id}_grant_type", "client_credentials");
    }
}