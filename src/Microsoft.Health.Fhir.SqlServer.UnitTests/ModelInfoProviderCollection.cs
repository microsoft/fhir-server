// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests
{
    /// <summary>
    /// Serializes the test classes that read or write the process-global <c>ModelInfoProvider</c>.
    /// </summary>
    /// <remarks>
    /// <c>ModelInfoProvider</c> is a plain static, so a class that replaces it races any class that
    /// is relying on it. Concretely, <c>SqlServerFhirDataStoreUnitTests</c> installs a provider that
    /// knows only the <c>Group</c> resource type and no compartments, while
    /// <c>CompartmentQueryGeneratorTests</c> and <c>ScalarTemporalEqualityRewriterTests</c> need the
    /// compartment-aware provider their class fixture installs. When those run concurrently the
    /// compartment tests fail with "Expected an expression that evaluates to true", and which tests
    /// fail varies from run to run.
    ///
    /// A class fixture cannot fix this, because it only orders the classes that share it and does
    /// nothing about a third class overwriting the same static. Sharing one collection is what
    /// actually serializes them: xUnit runs the classes within a collection one after another.
    /// <c>DisableParallelization</c> additionally keeps the collection from overlapping other
    /// collections, so a future class that touches the provider cannot reintroduce the race without
    /// also being added here.
    ///
    /// The race is latent rather than new, but xUnit v3 schedules these classes concurrently where
    /// v2 did not, so the migration is what makes it reproduce.
    /// </remarks>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class ModelInfoProviderCollection
    {
        /// <summary>
        /// The collection name to put on every test class that reads or writes the global provider.
        /// </summary>
        public const string Name = "ModelInfoProviderTests";
    }
}
