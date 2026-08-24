// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryVariants
{
    /// <summary>
    /// A class that is both expanded per data store and uses the retrying test attributes, which is
    /// the shape most of this repository's integration tests take.
    /// </summary>
    /// <remarks>
    /// Variant expansion and retrying are separate mechanisms that meet here: expansion rewrites the
    /// test method and its traits, while the retry discoverers build their own test case type and
    /// copy the traits across. Either one can drop what the other added, and the result is a variant
    /// that no leg selecting positively on a data store can see - so it never runs and the leg still
    /// reports success.
    /// </remarks>
    [AssetArgumentSets(AssetDataStore.Sql | AssetDataStore.Cosmos)]
    [Trait("Category", "RetryVariant")]
    public class RetryVariantTests : IClassFixture<AssetFixture>
    {
        private static readonly ConcurrentDictionary<string, int> Attempts = new ConcurrentDictionary<string, int>();

        private readonly AssetFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryVariantTests"/> class.
        /// </summary>
        /// <param name="fixture">The variant fixture injected by the custom executor.</param>
        public RetryVariantTests(AssetFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// A retrying fact that passes, so that its variants and traits can be asserted on.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10)]
        public void PassingRetryFact()
        {
            Assert.NotEqual(default, _fixture.DataStore);
        }

        /// <summary>
        /// A retrying fact that fails once and then passes, proving retries still happen inside an
        /// expanded variant and that each variant retries on its own count.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = true)]
        public void FlakyRetryFact()
        {
            int attempt = Attempts.AddOrUpdate(_fixture.DataStore.ToString(), 1, (_, current) => current + 1);

            if (attempt == 1)
            {
                Assert.Fail($"ASSET: {_fixture.DataStore} failing on attempt {attempt}");
            }
        }

        /// <summary>
        /// A retrying theory, so that a row of a theory is expanded per data store as well.
        /// </summary>
        /// <param name="value">Identifies the row.</param>
        [RetryTheory(MaxRetries = 2, DelayBetweenRetriesMs = 10)]
        [InlineData(1)]
        [InlineData(2)]
        public void RetryTheoryRow(int value)
        {
            Assert.InRange(value, 1, 2);
        }

#pragma warning disable xUnit1003 // Theory methods must have test data
#pragma warning disable xUnit1026 // Theory methods should use all of their parameters

        /// <summary>
        /// A retrying theory declaring no data, which xunit reports as an error case rather than a
        /// test. It has to reach a leg selecting positively on a data store, carrying the reason it
        /// could not run.
        /// </summary>
        /// <param name="value">Never supplied: the theory declares no data.</param>
        [RetryTheory]
        public void MalformedRetryTheory(int value)
        {
            Assert.True(true);
        }

#pragma warning restore xUnit1026
#pragma warning restore xUnit1003

        /// <summary>
        /// Supplies no rows at all, so that the theory below reaches xunit's error case through the
        /// data attribute rather than through having no data attribute.
        /// </summary>
        /// <returns>An empty sequence.</returns>
        public static TheoryData<int> NoRows()
        {
            return new TheoryData<int>();
        }

        /// <summary>
        /// A retrying theory whose data attribute yields no rows, which is the other way xunit ends
        /// up reporting a theory it cannot run. It has to reach a leg selecting positively on a data
        /// store, carrying the reason it could not run.
        /// </summary>
        /// <param name="value">Never supplied: the data attribute yields no rows.</param>
        [RetryTheory]
        [MemberData(nameof(NoRows))]
        public void EmptyDataRetryTheory(int value)
        {
            Assert.InRange(value, 1, 2);
        }
    }
}
