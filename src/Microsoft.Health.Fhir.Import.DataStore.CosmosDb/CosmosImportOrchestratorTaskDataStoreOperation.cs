// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Import.Core;

namespace Microsoft.Health.Fhir.Import.DataStore.CosmosDb
{
    public class CosmosImportOrchestratorTaskDataStoreOperation : IImportOrchestratorTaskDataStoreOperation
    {
        public Task PostprocessAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task PreprocessAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
