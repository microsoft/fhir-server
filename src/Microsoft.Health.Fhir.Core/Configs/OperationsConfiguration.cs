// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class OperationsConfiguration
    {
        public IList<HostingBackgroundServiceQueueItem> HostingBackgroundServiceQueues { get; } = new List<HostingBackgroundServiceQueueItem>();

        public ExportJobConfiguration Export { get; set; } = new ExportJobConfiguration();

        public ReindexJobConfiguration Reindex { get; set; } = new ReindexJobConfiguration();

        public ConvertDataConfiguration ConvertData { get; set; } = new ConvertDataConfiguration();

        public ValidateOperationConfiguration Validate { get; set; } = new ValidateOperationConfiguration();

        public IntegrationDataStoreConfiguration IntegrationDataStore { get; set; } = new IntegrationDataStoreConfiguration();

        public ImportTaskConfiguration Import { get; set; } = new ImportTaskConfiguration();

        /// <summary>
        /// Removes queues based on the enabled status of the operations.
        /// </summary>
        public void RemoveDisabledQueues()
        {
            if (!Export.Enabled || (string.IsNullOrEmpty(Export.StorageAccountConnection) && string.IsNullOrEmpty(Export.StorageAccountUri)))
            {
                HostingBackgroundServiceQueues
                    .Where(q => q.Queue == QueueType.Export)
                    .ToList()
                    .ForEach(q => HostingBackgroundServiceQueues.Remove(q));
            }

            if (!Import.Enabled || (string.IsNullOrEmpty(IntegrationDataStore.StorageAccountConnection) && string.IsNullOrEmpty(IntegrationDataStore.StorageAccountUri)))
            {
                HostingBackgroundServiceQueues
                    .Where(q => q.Queue == QueueType.Import)
                    .ToList()
                    .ForEach(q => HostingBackgroundServiceQueues.Remove(q));
            }
        }
    }
}
