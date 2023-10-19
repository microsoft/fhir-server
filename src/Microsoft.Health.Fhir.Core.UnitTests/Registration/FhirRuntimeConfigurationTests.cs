// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public void GivenARuntimeConfiguration_WhenForAzureApiForFHIR_FollowsTheExpectedValues()
        {
            IFhirRuntimeConfiguration runtimeConfiguration = new AzureApiForFhirRuntimeConfiguration();

            Assert.Equal(KnownDataStores.CosmosDb, runtimeConfiguration.DataStore);
            Assert.False(runtimeConfiguration.IsSelectiveSearchParameterSupported);
            Assert.True(runtimeConfiguration.IsExportBackgroundWorkedSupported);
            Assert.False(runtimeConfiguration.IsCustomerKeyValidationBackgroudWorkerSupported);
            Assert.False(runtimeConfiguration.IsTransactionSupported);
        }

        public void GivenARuntimeConfiguration_WhenForAzureHealthDataServices_FollowsTheExpectedValues()
        {
            IFhirRuntimeConfiguration runtimeConfiguration = new AzureHealthDataServicesRuntimeConfiguration();

            Assert.Equal(KnownDataStores.SqlServer, runtimeConfiguration.DataStore);
            Assert.True(runtimeConfiguration.IsSelectiveSearchParameterSupported);
            Assert.False(runtimeConfiguration.IsExportBackgroundWorkedSupported);
            Assert.True(runtimeConfiguration.IsCustomerKeyValidationBackgroudWorkerSupported);
            Assert.True(runtimeConfiguration.IsTransactionSupported);
        }
    }
}
