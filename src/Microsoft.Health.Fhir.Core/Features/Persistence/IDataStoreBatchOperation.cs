// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IDataStoreBatchOperation
    {
        Task ResetDataStoreAsync(CancellationToken cancellationToken);

        Task DisableIndexesAsync(CancellationToken cancellationToken);

        Task EnableIndexesAsync(CancellationToken cancellationToken);

        Task BulkImportAsync(IReadOnlyCollection<ResourceWrapper> inputResources);
    }
}
