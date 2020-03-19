// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public class TestApplication : IEquatable<TestApplication>
    {
        public TestApplication(string id)
        {
            Id = id;
        }

        private string Id { get; }

        public string ClientId => GetEnvironmentVariableWithDefault($"app_{Id}_id", Id);

        public string ClientSecret => GetEnvironmentVariableWithDefault($"app_{Id}_secret", Id);

        public string GrantType => GetEnvironmentVariableWithDefault($"app_{Id}_grant_type", "client_credentials");

        public bool Equals(TestApplication other)
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

        public override bool Equals(object obj) => Equals(obj as TestApplication);

        public override int GetHashCode() => (Id?.GetHashCode()).GetValueOrDefault();
    }
}
