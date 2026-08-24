// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Serialises the test classes that add, reindex, or delete search parameters, which share the
    /// server's search parameter registry and cannot run alongside each other.
    /// </summary>
    /// <remarks>
    /// This class deliberately carries no traits. xunit v3 puts a collection's traits on the tests of
    /// every class in the collection, so a category declared here would be inherited by every member
    /// - including members that only joined to be serialised. CI legs that exclude a category would
    /// then skip those members as well, and an exclusion filter reports success for what it did not
    /// select, so nothing in the leg's output would say the tests were dropped. Categories therefore
    /// belong on the individual test classes, never here.
    /// </remarks>
    [CollectionDefinition(Categories.IndexAndReindex, DisableParallelization = true)]
    public class IndexAndReindexCollection
    {
    }
}
