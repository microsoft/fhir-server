// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerFhirDataStoreBulkOperation : IDataStoreBatchOperation
    {
        public Task BulkImportAsync(IReadOnlyCollection<ResourceWrapper> inputResources)
        {
            throw new System.NotImplementedException();
        }

        public Task DisableIndexesAsync(CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        public Task EnableIndexesAsync(CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }

        public Task ResetDataStoreAsync(CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }
    }
}
