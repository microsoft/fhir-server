// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// Class to hold metadata for one query of a reindex job
    /// </summary>
    public class ReindexJobQueryStatusConverter : JsonConverter<ConcurrentDictionary<ReindexJobQueryStatus, byte>>
    {
        public override ConcurrentDictionary<ReindexJobQueryStatus, byte> ReadJson(JsonReader reader, Type objectType, [AllowNull] ConcurrentDictionary<ReindexJobQueryStatus, byte> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var queryStatusDictionary = new ConcurrentDictionary<ReindexJobQueryStatus, byte>();

            JArray queryStatusArray = JArray.Load(reader);
            foreach (var queryStatusJObject in queryStatusArray)
            {
                var queryStatus = queryStatusJObject.ToObject<ReindexJobQueryStatus>();
                queryStatusDictionary.TryAdd(queryStatus, 1);
            }

            return queryStatusDictionary;
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] ConcurrentDictionary<ReindexJobQueryStatus, byte> value, JsonSerializer serializer)
        {
            var queryStatusArray = new JArray();
            foreach (var queryStatus in value?.Keys)
            {
                queryStatusArray.Add(JObject.FromObject(queryStatus));
            }

            queryStatusArray.WriteTo(writer);
        }
    }
}
