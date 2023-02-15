// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

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
    }
}
