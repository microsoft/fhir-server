// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;

namespace Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage.StoredProcedures.UpdateUnsupportedSearchParametersToUnsupported
{
    public class UpdateUnsupportedSearchParameters : StoredProcedureBase
    {
        private const string _searchParameterStatusPartitionKey = "__searchparameterstatus__";

        public UpdateUnsupportedSearchParameters()
            : base(new UpdateUnsupportedSearchParametersMetadata())
        {
        }

        public async Task<StoredProcedureExecuteResponse<string>> Execute(Scripts client, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(client, nameof(client));

            return await ExecuteStoredProcAsync<string>(client, _searchParameterStatusPartitionKey, cancellationToken);
        }
    }
}
