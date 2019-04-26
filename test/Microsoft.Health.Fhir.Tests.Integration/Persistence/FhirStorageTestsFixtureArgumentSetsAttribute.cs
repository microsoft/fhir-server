// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Indicates which data stores tests should be verified with.
    /// Must be placed on test classes where the class fixture is parameterized with DataStore.
    /// Can optionally be placed on individual methods to override the class-level behavior.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false)]
    public sealed class FhirStorageTestsFixtureArgumentSetsAttribute : FixtureArgumentSetsAttribute
    {
        public FhirStorageTestsFixtureArgumentSetsAttribute(DataStore dataStores = 0)
            : base(dataStores)
        {
        }
    }
}
