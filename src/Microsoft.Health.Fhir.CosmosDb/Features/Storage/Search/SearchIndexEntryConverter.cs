// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    public class SearchIndexEntryConverter : JsonConverter
    {
        private static readonly ConcurrentQueue<SearchIndexEntryJObjectGenerator> CachedGenerators = new ConcurrentQueue<SearchIndexEntryJObjectGenerator>();

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(SearchIndexEntry);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // We don't currently support reading the search index from the Cosmos DB.
            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var searchIndexEntry = (SearchIndexEntry)value;

            // Cached the object generator for reuse.
            if (!CachedGenerators.TryDequeue(out SearchIndexEntryJObjectGenerator generator))
            {
                generator = new SearchIndexEntryJObjectGenerator();
            }

            if (searchIndexEntry.Value is CompositeSearchValue compositeValue)
            {
                // From the cartesian product, produce CompositeSearchValues where each component has exactly one value.
                foreach (IEnumerable<ISearchValue> compositeSearchValues in compositeValue.Components.CartesianProduct())
                {
                    WriteJsonImpl(writer, generator, searchIndexEntry.ParamName, new CompositeSearchValue(compositeSearchValues.Select(v => new[] { v }).ToArray()));
                }
            }
            else
            {
                WriteJsonImpl(writer, generator, searchIndexEntry.ParamName, searchIndexEntry.Value);
            }
        }

        private static void WriteJsonImpl(JsonWriter writer, SearchIndexEntryJObjectGenerator generator, string paramName, ISearchValue searchValue)
        {
            JObject generatedObj;

            try
            {
                searchValue.AcceptVisitor(generator);

                generatedObj = generator.Output;
            }
            finally
            {
                generator.Reset();

                CachedGenerators.Enqueue(generator);
            }

            generatedObj.AddFirst(
                new JProperty(SearchValueConstants.ParamName, paramName));

            generatedObj.WriteTo(writer);
        }
    }
}
