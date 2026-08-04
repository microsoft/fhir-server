// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    /// <summary>
    /// Measures the per-manager managed-memory projection of <see cref="SearchParameterDefinitionManager"/> when
    /// many tenant-scoped managers are built from one shared <see cref="EmbeddedSearchParameterDefinitionSource"/>.
    /// </summary>
    /// <remarks>
    /// This is not a test of an isolated dictionary size. Each <see cref="SearchParameterDefinitionManager"/>
    /// allocates its own <c>UrlLookup</c> (definition-url to <see cref="SearchParameterInfo"/>) and <c>TypeLookup</c>
    /// (resource-type to code to ordered definition-url queue) state during construction via
    /// <c>SearchParameterDefinitionBuilder.Build</c>. The reported per-manager figure below is therefore dominated by
    /// those two populated concurrent-collection graphs, not by any single dictionary or the shared, reused
    /// dependency graph (mediator, search service, comparer, status store, data store) which is intentionally
    /// created once and passed to every manager so its allocation cost is never attributed to per-manager growth.
    /// This attribution intentionally differs from the plan's per-manager-everything-else setup and biases the
    /// manager projection downward by excluding non-manager test-scaffolding allocations rather than hiding them.
    /// Parallelization is disabled for this collection so GC.GetTotalMemory readings are not perturbed by unrelated
    /// concurrently-running tests in the same process.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [CollectionDefinition(CollectionName, DisableParallelization = true)]
    [Collection(CollectionName)]
    public class SearchParameterManagerFootprintTests
    {
        private const string CollectionName = "SearchParameterManagerFootprintTests";

        // Kept modest and explicitly odd so the suite stays fast in CI while still giving a defensible,
        // outlier-resistant observed middle value rather than averaging two trials or using one noisy sample.
        private const int WarmupManagerCount = 3;
        private const int TrialCount = 5;
        private const int ManagersPerTrial = 8;

        // One shared source: the embedded search-parameter bundles are parsed once (lazily, on first use)
        // and every manager below builds its own UrlLookup/TypeLookup projection over that same parsed set.
        private static readonly ISearchParameterDefinitionSource SharedSource =
            new EmbeddedSearchParameterDefinitionSource(ModelInfoProvider.Instance);

        // The intentionally inert NSubstitute dependencies remain shared by every manager constructed in this
        // fixture. Unlike the plan's per-manager-everything-else setup, this deliberately excludes non-manager
        // test-scaffolding allocations and therefore biases the reported manager projection downward.
        private static readonly IMediator SharedMediator = Substitute.For<IMediator>();

        private static readonly IScopeProvider<ISearchService> SharedSearchServiceScopeProvider =
            Substitute.For<ISearchService>().CreateMockScopeProvider();

        // Use one shared real production comparer. Unlike the substitute it replaces, it does not retain a call
        // record for every Compare invocation during manager construction.
        private static readonly SearchParameterComparer SharedComparer =
            new SearchParameterComparer(NullLogger<ISearchParameterComparer<SearchParameterInfo>>.Instance);

        private static readonly IScopeProvider<ISearchParameterStatusDataStore> SharedStatusDataStoreScopeProvider =
            Substitute.For<ISearchParameterStatusDataStore>().CreateMockScopeProvider();

        private static readonly IScopeProvider<IFhirDataStore> SharedFhirDataStoreScopeProvider =
            Substitute.For<IFhirDataStore>().CreateMockScopeProvider();

        private readonly ITestOutputHelper _output;

        public SearchParameterManagerFootprintTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void GivenManyManagersFromOneSharedSource_WhenConstructed_ThenEachManagerAndItsLookupStateAreDistinctAndPopulated()
        {
            // Arrange & Act: build a small, assertable sample of managers over the shared source/dependencies.
            var managers = new List<SearchParameterDefinitionManager>();
            for (int i = 0; i < WarmupManagerCount; i++)
            {
                managers.Add(CreateManager());
            }

            // Assert: every manager instance is distinct.
            for (int i = 0; i < managers.Count; i++)
            {
                for (int j = i + 1; j < managers.Count; j++)
                {
                    Assert.NotSame(managers[i], managers[j]);

                    // Each manager owns its own UrlLookup/TypeLookup graph, not a shared reference.
                    Assert.NotSame(managers[i].UrlLookup, managers[j].UrlLookup);
                    Assert.NotSame(managers[i].TypeLookup, managers[j].TypeLookup);
                }
            }

            int searchParameterCount = 0;
            foreach (SearchParameterDefinitionManager manager in managers)
            {
                Assert.NotNull(manager.UrlLookup);
                Assert.NotNull(manager.TypeLookup);
                Assert.NotEmpty(manager.UrlLookup);
                Assert.NotEmpty(manager.TypeLookup);

                int count = manager.AllSearchParameters.Count();
                Assert.True(count > 0, "Discovered search-parameter count for a manager must be greater than zero.");
                searchParameterCount = count;
            }

            _output.WriteLine($"Managers constructed and verified distinct/populated: {managers.Count}");
            _output.WriteLine($"Search parameters discovered per manager (UrlLookup population): {searchParameterCount}");

            Assert.True(searchParameterCount > 0, "Discovered search-parameter count must be greater than zero.");

            GC.KeepAlive(managers);
        }

        [Fact]
        public void GivenRepeatedTrials_WhenManyManagersAreConstructedOverSharedDependencies_ThenMedianManagedMemoryPerManagerIsReportedHonestly()
        {
            Assert.True(TrialCount > 0 && TrialCount % 2 == 1, "TrialCount must be positive and odd so the median is one observed raw trial result.");

            // Warm up JIT and any lazily-initialized statics (embedded bundle parsing, comparer caches, etc.)
            // before taking any baseline measurement so the first trial is not penalized by one-time costs.
            var warmupManagers = new List<SearchParameterDefinitionManager>();
            for (int i = 0; i < WarmupManagerCount; i++)
            {
                warmupManagers.Add(CreateManager());
            }

            int warmupSearchParameterCount = warmupManagers[0].AllSearchParameters.Count();
            GC.KeepAlive(warmupManagers);
            warmupManagers = null;

            ForceFullCollection();

            var perManagerBytesByTrial = new List<double>(TrialCount);
            var rawTrialDeltaBytesByTrial = new List<long>(TrialCount);

            for (int trial = 0; trial < TrialCount; trial++)
            {
                ForceFullCollection();
                long before = GC.GetTotalMemory(forceFullCollection: true);

                var trialManagers = new List<SearchParameterDefinitionManager>(ManagersPerTrial);
                for (int i = 0; i < ManagersPerTrial; i++)
                {
                    trialManagers.Add(CreateManager());
                }

                long after = GC.GetTotalMemory(forceFullCollection: true);

                // Keep every manager reachable until after the measurement above so none of them (or their
                // UrlLookup/TypeLookup graphs) can be collected before GC.GetTotalMemory observes them.
                GC.KeepAlive(trialManagers);

                long rawTrialDeltaBytes = after - before;
                double rawManagerDeltaBytes = rawTrialDeltaBytes / (double)ManagersPerTrial;
                Assert.True(
                    rawManagerDeltaBytes > 0,
                    $"Raw managed-memory delta per manager for trial {trial + 1} must be positive; observed {rawManagerDeltaBytes} bytes.");

                rawTrialDeltaBytesByTrial.Add(rawTrialDeltaBytes);
                perManagerBytesByTrial.Add(rawManagerDeltaBytes);

                Assert.NotSame(trialManagers[0], trialManagers[trialManagers.Count - 1]);
                Assert.NotSame(trialManagers[0].UrlLookup, trialManagers[trialManagers.Count - 1].UrlLookup);
                Assert.NotSame(trialManagers[0].TypeLookup, trialManagers[trialManagers.Count - 1].TypeLookup);
                Assert.All(trialManagers, m => Assert.NotEmpty(m.UrlLookup));
                Assert.All(trialManagers, m => Assert.NotEmpty(m.TypeLookup));

                trialManagers = null;
            }

            ForceFullCollection();

            // The manager projection remains report-only: GC noise and the plan's no-flat-threshold rationale do not
            // support a stable upper bound.
            double median = MedianOfOddCount(perManagerBytesByTrial);

            _output.WriteLine("SearchParameterDefinitionManager projection (UrlLookup + TypeLookup dominated)");
            _output.WriteLine($"Trials: {TrialCount}, managers per trial: {ManagersPerTrial}, total managers constructed for measurement: {TrialCount * ManagersPerTrial}");
            _output.WriteLine($"Search parameters discovered per manager: {warmupSearchParameterCount}");
            _output.WriteLine($"Raw aggregate managed-memory delta per trial (bytes): [{string.Join(", ", rawTrialDeltaBytesByTrial.Select(bytes => bytes.ToString(CultureInfo.InvariantCulture)))}]");
            _output.WriteLine($"Per-trial managed-memory delta per manager (bytes): [{string.Join(", ", perManagerBytesByTrial.Select(bytes => bytes.ToString("R", CultureInfo.InvariantCulture)))}]");
            _output.WriteLine($"Median managed-memory projection per manager (bytes): {median.ToString("R", CultureInfo.InvariantCulture)}");

            Assert.True(warmupSearchParameterCount > 0, "Discovered search-parameter count must be greater than zero.");
            Assert.Equal(TrialCount, perManagerBytesByTrial.Count);
            Assert.Equal(TrialCount, rawTrialDeltaBytesByTrial.Count);
        }

        private static void ForceFullCollection()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }

        private static double MedianOfOddCount(IReadOnlyCollection<double> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Count == 0 || values.Count % 2 == 0)
            {
                throw new ArgumentException("Median requires a nonempty odd number of raw trial results.", nameof(values));
            }

            List<double> sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        // Every manager below is a distinct production-shaped instance: it owns its own UrlLookup/TypeLookup
        // state (populated in the constructor via SearchParameterDefinitionBuilder.Build), while reusing the
        // single shared dependency graph declared above so that graph's allocation is never counted as per-manager
        // growth. The comparer is the real production implementation; only the other shared dependencies are inert
        // substitutes.
        private static SearchParameterDefinitionManager CreateManager()
        {
            return new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                SharedSource,
                SharedMediator,
                SharedSearchServiceScopeProvider,
                SharedComparer,
                SharedStatusDataStoreScopeProvider,
                SharedFhirDataStoreScopeProvider,
                NullLogger<SearchParameterDefinitionManager>.Instance);
        }
    }
}
