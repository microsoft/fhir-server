// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class FhirRuntimeConfigurationTests
    {
        [Theory]
        [InlineData(FhirRuntimeState.Deprecated)]
        [InlineData(FhirRuntimeState.Active)]
        public void GivenARuntimeConfiguration_WhenForAzureApiForFHIR_FollowsTheExpectedValues(FhirRuntimeState runtimeState)
        {
            // Azure API For FHIR.
            IFhirRuntimeConfiguration runtimeConfiguration = new AzureApiForFhirRuntimeConfiguration(runtimeState);

            // Support to Cosmos Db.
            Assert.Equal(KnownDataStores.CosmosDb, runtimeConfiguration.DataStore);

            // Runtime state should follow the value used as part of the initialization.
            Assert.Equal(runtimeState, runtimeConfiguration.RuntimeState);

            // No support to Selective Search Parameter.
            Assert.False(runtimeConfiguration.IsSelectiveSearchParameterSupported);

            // No support to CMK Background Service.
            Assert.False(runtimeConfiguration.IsCustomerKeyValidationBackgroundWorkerSupported);

            // No support to transactions.
            Assert.False(runtimeConfiguration.IsTransactionSupported);

            // No support to Surrogate Id Ranging.
            Assert.False(runtimeConfiguration.IsSurrogateIdRangingSupported);

            // No support to Query Cache.
            Assert.False(runtimeConfiguration.IsQueryCacheSupported);

            // Support to Latency over Efficiency.
            Assert.True(runtimeConfiguration.IsLatencyOverEfficiencySupported);
        }

        [Fact]
        public void GivenARuntimeConfiguration_WhenForAzureHealthDataServices_FollowsTheExpectedValues()
        {
            // Azure Health Data Services.
            IFhirRuntimeConfiguration runtimeConfiguration = new AzureHealthDataServicesRuntimeConfiguration();

            // Support to SQL Server.
            Assert.Equal(KnownDataStores.SqlServer, runtimeConfiguration.DataStore);

            // Runtime state is active.
            Assert.Equal(FhirRuntimeState.Active, runtimeConfiguration.RuntimeState);

            // Support to Selective Search Parameter.
            Assert.True(runtimeConfiguration.IsSelectiveSearchParameterSupported);

            // Support to CMK Background Service.
            Assert.True(runtimeConfiguration.IsCustomerKeyValidationBackgroundWorkerSupported);

            // Support to transactions.
            Assert.True(runtimeConfiguration.IsTransactionSupported);

            // Support to Surrogate Id Ranging.
            Assert.True(runtimeConfiguration.IsSurrogateIdRangingSupported);

            // Support to Query Cache.
            Assert.True(runtimeConfiguration.IsQueryCacheSupported);

            // No support to Latency over Efficiency.
            Assert.False(runtimeConfiguration.IsLatencyOverEfficiencySupported);
        }
    }
}
