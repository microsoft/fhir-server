// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class TestUser
    {
        public TestUser(string id)
        {
            Id = id;
        }

        private string Id { get; }

        public string UserId => GetEnvironmentVariableWithDefault($"user_{Id}_id", Id);

        public string Password => GetEnvironmentVariableWithDefault($"user_{Id}_secret", Id);

        public string GrantType => "password";
    }
}
