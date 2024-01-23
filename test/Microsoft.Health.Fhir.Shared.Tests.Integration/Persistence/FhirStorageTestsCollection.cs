// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [CollectionDefinition("FhirStorageTestsCollection")]
    public class FhirStorageTestsCollection : ICollectionFixture<FhirStorageTestsFixture>
    {
        // No code needed here
    }
}
