// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Health.Fhir.Core.Features.Search;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// JsonConverter to handle <see cref="ReindexJobRecord.ResourceCounts"> from the legacy version with ‹string, int› to the current version with ‹string, SearchResultReindex›.
    /// </summary>
    public class ReindexJobQueryResourceCountsConverter : JsonConverter<ConcurrentDictionary<string, SearchResultReindex>>
    {
        public override ConcurrentDictionary<string, SearchResultReindex> ReadJson(JsonReader reader, Type objectType, [AllowNull] ConcurrentDictionary<string, SearchResultReindex> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var resultReindexDictionary = new ConcurrentDictionary<string, SearchResultReindex>();

            var settings = new JsonSerializerSettings
            {
                Error = (sender, args) => { args.ErrorContext.Handled = true; },
                MissingMemberHandling = MissingMemberHandling.Error,
            };

            var jObject = JObject.Load(reader);

            var legacyObject = JsonConvert.DeserializeObject<ConcurrentDictionary<string, int>>(jObject.ToString(), settings);

            if (legacyObject != null)
            {
                foreach (var kvp in legacyObject.Keys)
                {
                    resultReindexDictionary.TryAdd(kvp, new SearchResultReindex(legacyObject[kvp]));
                }
            }
            else
            {
                var currentObject = JsonConvert.DeserializeObject<ConcurrentDictionary<string, SearchResultReindex>>(jObject.ToString(), settings);

                if (currentObject != null)
                {
                    resultReindexDictionary = currentObject;
                }
            }

            return resultReindexDictionary;
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] ConcurrentDictionary<string, SearchResultReindex> value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            foreach (var keyValuePair in value)
            {
                writer.WritePropertyName(keyValuePair.Key);
                serializer.Serialize(writer, keyValuePair.Value);
            }

            writer.WriteEndObject();
        }
    }
}
