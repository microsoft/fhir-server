// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestUser : IEquatable<TestUser>
    {
        public TestUser(string id)
        {
            Id = id;
        }

        private string Id { get; }

        public string UserId => GetEnvironmentVariableWithDefault($"user_{Id}_id", Id);

        public string Password => GetEnvironmentVariableWithDefault($"user_{Id}_secret", Id);

        public string GrantType => "password";

        public bool Equals(TestUser other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Id == other.Id;
        }

        public override bool Equals(object obj) => Equals((TestUser)obj);

        public override int GetHashCode() => (Id?.GetHashCode()).GetValueOrDefault();
    }
}
