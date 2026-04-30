// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Disables parallelization for CustomQueries tests due to shared static state.
    /// </summary>
    [CollectionDefinition("CustomQueriesTests", DisableParallelization = true)]
    public sealed class CustomQueriesTestsCollection
    {
    }
}
