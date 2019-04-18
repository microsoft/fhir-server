// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence.InMemory;
using Microsoft.Health.Fhir.Tests.Common.Persistence;

namespace Microsoft.Health.Fhir.Core.UnitTests.Persistence
{
    public class InMemoryStorageTests : FhirStorageTestsBase
    {
        public InMemoryStorageTests()
            : base(new InMemoryFhirDataStore())
        {
        }
    }
}
