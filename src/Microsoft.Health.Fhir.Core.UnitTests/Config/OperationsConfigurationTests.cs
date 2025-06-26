// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class OperationsConfigurationTests
    {
        [Fact]
        public void GivenAnOperationsConfiguration_WhenJobConfigsAreSet_PropertiesAreAccessible()
        {
            // Arrange
            var operationsConfig = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = true, StorageAccountConnection = "test-connection-string" },
                Import = new ImportJobConfiguration { Enabled = false },
                IntegrationDataStore = new IntegrationDataStoreConfiguration { StorageAccountConnection = "test-connection-string" },
            };

            // Assert
            Assert.True(operationsConfig.Export.Enabled);
            Assert.False(operationsConfig.Import.Enabled);
            Assert.Equal("test-connection-string", operationsConfig.Export.StorageAccountConnection);
            Assert.Equal("test-connection-string", operationsConfig.IntegrationDataStore.StorageAccountConnection);
        }
    }
}
