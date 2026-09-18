// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets
{
    /// <summary>
    /// Reproduces the attribute shape found on the real IndexAndReindex E2E classes: two classes in one assembly
    /// each declare a collection with the same name, and only one of them joins it. Used to observe what the real
    /// discoverer does with that shape.
    /// </summary>
    /// <remarks>
    /// These methods are discovery fixtures, not real tests. Their bodies are intentionally empty.
    /// </remarks>
    [CollectionDefinition(DuplicateCollectionName, DisableParallelization = true)]
    public class DeclaresCollectionButDoesNotJoinIt
    {
        public const string DuplicateCollectionName = "DuplicatedCollectionName";

        [Fact]
        public void DeclaringClassFact()
        {
        }
    }

    /// <summary>
    /// Declares the same collection name a second time and also joins it.
    /// </summary>
    [CollectionDefinition(DeclaresCollectionButDoesNotJoinIt.DuplicateCollectionName, DisableParallelization = true)]
    [Collection(DeclaresCollectionButDoesNotJoinIt.DuplicateCollectionName)]
    public class DeclaresAndJoinsCollection
    {
        [Fact]
        public void JoiningClassFact()
        {
        }
    }
}
