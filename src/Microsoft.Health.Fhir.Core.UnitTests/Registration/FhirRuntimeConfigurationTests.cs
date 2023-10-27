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
        [Fact]
        public void GivenARuntimeConfiguration_WhenForAzureApiForFHIR_FollowsTheExpectedValues()
        {
            // Azure API For FHIR.
            IFhirRuntimeConfiguration runtimeConfiguration = new AzureApiForFhirRuntimeConfiguration();

            // Support to Cosmos Db.
            Assert.Equal(KnownDataStores.CosmosDb, runtimeConfiguration.DataStore);

            // No support to Selective Search Parameter.
            Assert.False(runtimeConfiguration.IsSelectiveSearchParameterSupported);

            // Support to Export.
            Assert.True(runtimeConfiguration.IsExportBackgroundWorkerSupported);

            // No support to CMK Background Service.
            Assert.False(runtimeConfiguration.IsCustomerKeyValidationBackgroudWorkerSupported);

            // No support to transactions.
            Assert.False(runtimeConfiguration.IsTransactionSupported);
        }

        [Fact]
        public void GivenARuntimeConfiguration_WhenForAzureHealthDataServices_FollowsTheExpectedValues()
        {
            // Azure Health Data Services.
            IFhirRuntimeConfiguration runtimeConfiguration = new AzureHealthDataServicesRuntimeConfiguration();

            // Support to SQL Server.
            Assert.Equal(KnownDataStores.SqlServer, runtimeConfiguration.DataStore);

            // Support to Selective Search Parameter.
            Assert.True(runtimeConfiguration.IsSelectiveSearchParameterSupported);

            // No support to Export.
            Assert.False(runtimeConfiguration.IsExportBackgroundWorkerSupported);

            // Support to CMK Background Service.
            Assert.True(runtimeConfiguration.IsCustomerKeyValidationBackgroudWorkerSupported);

            // Support to transactions.
            Assert.True(runtimeConfiguration.IsTransactionSupported);
        }
    }
}
