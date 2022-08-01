// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class OperationsConfiguration
    {
        public ExportJobConfiguration Export { get; set; } = new ExportJobConfiguration();

        public ReindexJobConfiguration Reindex { get; set; } = new ReindexJobConfiguration();

        public ConvertDataConfiguration ConvertData { get; set; } = new ConvertDataConfiguration();

        public TerminologyOperationConfiguration Terminology { get; set; } = new TerminologyOperationConfiguration();

        public IntegrationDataStoreConfiguration IntegrationDataStore { get; set; } = new IntegrationDataStoreConfiguration();

        public ImportTaskConfiguration Import { get; set; } = new ImportTaskConfiguration();
    }
}
