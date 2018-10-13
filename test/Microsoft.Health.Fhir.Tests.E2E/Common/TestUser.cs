// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestUser
    {
        private readonly string _id;

        public TestUser(string id)
        {
            _id = id;
        }

        public string Id => _id;

        public string Password => GetEnvironmentVariableWithDefault($"user_{_id}_password", GetEnvironmentVariableWithDefault($"user_all_password", _id));

        public string GrantType => GetEnvironmentVariableWithDefault($"user_{_id}_grant_type", "password");
    }
}
