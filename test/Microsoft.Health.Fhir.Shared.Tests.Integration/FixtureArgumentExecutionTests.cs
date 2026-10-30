// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration
{
    /// <summary>
    /// Verifies discovery and execution of fixture argument variants without a database connection.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.AssemblyValidation)]
    public sealed class FixtureArgumentExecutionTests : IClassFixture<FixtureArgumentExecutionTests.DataStoreFixture>
    {
        private readonly DataStoreFixture _fixture;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixtureArgumentExecutionTests"/> class.
        /// </summary>
        /// <param name="fixture">The fixture created for the selected data store.</param>
        public FixtureArgumentExecutionTests(DataStoreFixture fixture)
        {
            _fixture = fixture;
        }

        /// <summary>
        /// Verifies that each class variant receives one data store rather than the combined flags.
        /// </summary>
        [Fact]
        public void GivenClassVariants_WhenExecuted_ThenEachReceivesASingleDataStore()
        {
            Assert.Contains(_fixture.DataStore, new[] { DataStore.CosmosDb, DataStore.SqlServer });
        }

        /// <summary>
        /// Verifies that a method override supplies its data store to the class fixture.
        /// </summary>
        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public void GivenMethodVariant_WhenExecuted_ThenItOverridesTheClassDataStores()
        {
            Assert.Equal(DataStore.SqlServer, _fixture.DataStore);
        }

        /// <summary>
        /// Verifies that theory arguments and fixture arguments are both preserved.
        /// </summary>
        /// <param name="expected">The data store expected for this theory.</param>
        [Theory]
        [InlineData(DataStore.CosmosDb)]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public void GivenTheoryVariant_WhenExecuted_ThenItPreservesTheArguments(DataStore expected)
        {
            Assert.Equal(expected, _fixture.DataStore);
        }

        /// <summary>
        /// Captures the fixture argument supplied by the custom test framework.
        /// </summary>
        public sealed class DataStoreFixture
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="DataStoreFixture"/> class.
            /// </summary>
            /// <param name="dataStore">The data store selected for this variant.</param>
            public DataStoreFixture(DataStore dataStore)
            {
                DataStore = dataStore;
            }

            /// <summary>
            /// Gets the data store selected for this fixture.
            /// </summary>
            public DataStore DataStore { get; }
        }
    }
}
