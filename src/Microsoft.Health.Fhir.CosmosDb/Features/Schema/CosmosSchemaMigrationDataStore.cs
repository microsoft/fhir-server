// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Schema;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Schema
{
    internal class CosmosSchemaMigrationDataStore : ISchemaMigrationDataStore
    {
        public Task<int> GetLatestCompatibleVersionAsync(int maxVersion, CancellationToken cancellationToken)
        {
            throw new OperationNotImplementedException(string.Format(Resources.NotSupportedException));
        }
    }
}
