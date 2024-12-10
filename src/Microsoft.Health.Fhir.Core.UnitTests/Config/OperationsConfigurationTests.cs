// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
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
        public void GivenAnOperationsConfiguration_WhenQueuesAreDisabled_RemoveThemFromTheList()
        {
            // Arrange
            var operationsConfig = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = false },
                Import = new ImportTaskConfiguration { Enabled = false },
            };

            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Export, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Import, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.BulkDelete, MaxRunningTaskCount = 1 });

            // Act
            operationsConfig.RemoveDisabledQueues();

            // Assert
            Assert.DoesNotContain(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Export);
            Assert.DoesNotContain(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Import);
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.BulkDelete);
        }

        [Fact]
        public void GivenAnOperationsConfiguration_WhenQueuesAreEnabledWithConnectionString_KeepThemInTheList()
        {
            // Arrange
            var operationsConfig = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = true, StorageAccountConnection = "test-connection-string" },
                Import = new ImportTaskConfiguration { Enabled = true },
                IntegrationDataStore = new IntegrationDataStoreConfiguration { StorageAccountConnection = "test-connection-string" },
            };

            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Export, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Import, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.BulkDelete, MaxRunningTaskCount = 1 });

            // Act
            operationsConfig.RemoveDisabledQueues();

            // Assert
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Export);
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Import);
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.BulkDelete);
        }

        [Fact]
        public void GivenAnOperationsConfiguration_WhenQueuesAreEnabledWithUri_KeepThemInTheList()
        {
            // Arrange
            var operationsConfig = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = true, StorageAccountUri = "https://test-account.blob.core.windows.net" },
                Import = new ImportTaskConfiguration { Enabled = true },
                IntegrationDataStore = new IntegrationDataStoreConfiguration { StorageAccountUri = "https://test-account.blob.core.windows.net" },
            };

            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Export, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Import, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.BulkDelete, MaxRunningTaskCount = 1 });

            // Act
            operationsConfig.RemoveDisabledQueues();

            // Assert
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Export);
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Import);
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.BulkDelete);
        }

        [Fact]
        public void GivenAnOperationsConfiguration_WhenNoQueuesExist_NoExceptionIsThrown()
        {
            // Arrange
            var operationsConfig = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = false },
                Import = new ImportTaskConfiguration { Enabled = false },
            };

            // Act & Assert
            var exception = Record.Exception(() => operationsConfig.RemoveDisabledQueues());
            Assert.Null(exception);
        }

        [Fact]
        public void GivenAnOperationsConfiguration_WhenMixedQueuesAreEnabledOrDisabled_HandleCorrectly()
        {
            // Arrange
            var operationsConfigWithExport = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = true, StorageAccountUri = "https://test-account.blob.core.windows.net" },
                Import = new ImportTaskConfiguration { Enabled = false },
            };

            var operationsConfigWithImport = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = false },
                Import = new ImportTaskConfiguration { Enabled = true},
                IntegrationDataStore = new IntegrationDataStoreConfiguration { StorageAccountUri = "https://test-account.blob.core.windows.net" },
            };

            operationsConfigWithExport.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Export, MaxRunningTaskCount = 2 });
            operationsConfigWithExport.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Import, MaxRunningTaskCount = 2 });
            operationsConfigWithExport.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.BulkDelete, MaxRunningTaskCount = 1 });

            operationsConfigWithImport.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Export, MaxRunningTaskCount = 2 });
            operationsConfigWithImport.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Import, MaxRunningTaskCount = 2 });
            operationsConfigWithImport.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.BulkDelete, MaxRunningTaskCount = 1 });

            // Act
            operationsConfigWithExport.RemoveDisabledQueues();

            operationsConfigWithImport.RemoveDisabledQueues();

            // Assert
            Assert.Contains(operationsConfigWithExport.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Export);
            Assert.DoesNotContain(operationsConfigWithExport.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Import);
            Assert.Contains(operationsConfigWithExport.HostingBackgroundServiceQueues, q => q.Queue == QueueType.BulkDelete);

            Assert.Contains(operationsConfigWithImport.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Import);
            Assert.DoesNotContain(operationsConfigWithImport.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Export);
            Assert.Contains(operationsConfigWithImport.HostingBackgroundServiceQueues, q => q.Queue == QueueType.BulkDelete);
        }

        [Fact]
        public void GivenAnOperationsConfiguration_WhenStorageStringsAreEmpty_RemoveQueues()
        {
            // Arrange
            var operationsConfig = new OperationsConfiguration
            {
                Export = new ExportJobConfiguration { Enabled = true, StorageAccountConnection = string.Empty, StorageAccountUri = string.Empty },
                Import = new ImportTaskConfiguration { Enabled = true },
                IntegrationDataStore = new IntegrationDataStoreConfiguration { StorageAccountConnection = string.Empty, StorageAccountUri = string.Empty },
            };

            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Export, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.Import, MaxRunningTaskCount = 2 });
            operationsConfig.HostingBackgroundServiceQueues.Add(new HostingBackgroundServiceQueueItem { Queue = QueueType.BulkDelete, MaxRunningTaskCount = 1 });

            // Act
            operationsConfig.RemoveDisabledQueues();

            // Assert
            Assert.DoesNotContain(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Export);
            Assert.DoesNotContain(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.Import);
            Assert.Contains(operationsConfig.HostingBackgroundServiceQueues, q => q.Queue == QueueType.BulkDelete);
        }
    }
}
