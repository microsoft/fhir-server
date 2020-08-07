﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbFhirStorageTestHelper : IFhirStorageTestHelper
    {
        private const string ExportJobPartitionKey = "ExportJob";

        private readonly Container _documentClient;

        public CosmosDbFhirStorageTestHelper(Container documentClient)
        {
            _documentClient = documentClient;
        }

        public async Task DeleteAllExportJobRecordsAsync(CancellationToken cancellationToken = default)
        {
            var query = _documentClient.GetItemQueryIterator<JObject>(
                new QueryDefinition("SELECT doc.id FROM doc"),
                requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(ExportJobPartitionKey), });

            while (query.HasMoreResults)
            {
                var documents = await query.ReadNextAsync();

                foreach (dynamic doc in documents)
                {
                    await _documentClient.DeleteItemStreamAsync((string)doc.id, new PartitionKey(ExportJobPartitionKey), cancellationToken: cancellationToken);
                }
            }
        }

        public async Task DeleteExportJobRecordAsync(string id, CancellationToken cancellationToken = default)
        {
            await _documentClient.DeleteItemStreamAsync(id, new PartitionKey(ExportJobPartitionKey), cancellationToken: cancellationToken);
        }

        public async Task DeleteSearchParameterStatusAsync(string uri, CancellationToken cancellationToken = default)
        {
            await _documentClient.DeleteItemStreamAsync(uri.ComputeHash(), new PartitionKey(SearchParameterStatusWrapper.SearchParameterStatusPartitionKey), cancellationToken: cancellationToken);
        }

        async Task<object> IFhirStorageTestHelper.GetSnapshotToken()
        {
            var documentQuery = _documentClient.GetItemQueryIterator<Tuple<int>>(
                "SELECT top 1 c._ts as Item1 FROM c ORDER BY c._ts DESC");

            while (documentQuery.HasMoreResults)
            {
                foreach (Tuple<int> ts in await documentQuery.ReadNextAsync())
                {
                    return ts.Item1;
                }
            }

            return null;
        }

        async Task IFhirStorageTestHelper.ValidateSnapshotTokenIsCurrent(object snapshotToken)
        {
            Assert.True((int)await ((IFhirStorageTestHelper)this).GetSnapshotToken() <= (int)snapshotToken);
        }
    }
}
