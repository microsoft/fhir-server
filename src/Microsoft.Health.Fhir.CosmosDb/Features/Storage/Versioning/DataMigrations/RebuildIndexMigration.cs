// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations
{
    public abstract class RebuildIndexMigration : Migration
    {
        private readonly ISearchIndexer _indexer;
        private readonly JsonSerializer _serializer;

        protected RebuildIndexMigration(ISearchIndexer indexer)
        {
            EnsureArg.IsNotNull(indexer, nameof(indexer));

            _indexer = indexer;

            JsonSerializerSettings jsonSerializerSettings = DocumentClientInitializer.JsonSerializerSettings;
            jsonSerializerSettings.Converters.Add(new SearchIndexEntryConverter());

            _serializer = JsonSerializer.Create(jsonSerializerSettings);
        }

        public override IEnumerable<IUpdateOperation> Migrate(Document wrapper)
        {
            EnsureArg.IsNotNull(wrapper, nameof(wrapper));

            ResourceWrapper resourceWrapper = (dynamic)wrapper;

            var resource = ResourceDeserializer.Deserialize(resourceWrapper);
            var index = _indexer.Extract(resource);
            var updatedIndex = JArray.FromObject(index, _serializer);

            yield return new UpdateOperation(KnownResourceWrapperProperties.SearchIndices, updatedIndex);
        }
    }
}
