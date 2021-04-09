// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Reindex
{
    public class SqlBulkImporter : IBulkImporter<BulkImportResourceWrapper>
    {
        public Task ImportResourceAsync(Channel<BulkImportResourceWrapper> inputChannel, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }
    }
}
