// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestUser
    {
        private string _userId;

        public TestUser(string id)
        {
            _userId = id;
        }

        public string Id => GetEnvironmentVariableWithDefault($"user_{_userId}_id", _userId);

        public string Password => GetEnvironmentVariableWithDefault($"user_{_userId}_secret", _userId);

        public string GrantType => "password";
    }
}
