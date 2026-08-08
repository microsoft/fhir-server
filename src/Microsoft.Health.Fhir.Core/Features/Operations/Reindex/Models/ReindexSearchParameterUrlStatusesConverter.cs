// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models
{
    /// <summary>
    /// JsonConverter for <see cref="ReindexProcessingJobDefinition.SearchParameterUrlStatuses"/>.
    /// Writes enum values as names while preserving tuple shape.
    /// </summary>
    public class ReindexSearchParameterUrlStatusesConverter : JsonConverter<IReadOnlyCollection<(string Url, SearchParameterStatus Status)>>
    {
        public override IReadOnlyCollection<(string Url, SearchParameterStatus Status)> ReadJson(JsonReader reader, Type objectType, [AllowNull] IReadOnlyCollection<(string Url, SearchParameterStatus Status)> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                throw new JsonSerializationException("SearchParameterUrlStatuses must not be null.");
            }

            return JArray.Load(reader)
                .Select(token => token.ToObject<JObject>())
                .Select(statusObject => (
                    Url: statusObject["Url"].ToObject<string>(),
                    Status: Enum.Parse<SearchParameterStatus>(statusObject["Status"].ToObject<string>(), ignoreCase: true)))
                .ToList();
        }

        public override void WriteJson(JsonWriter writer, IReadOnlyCollection<(string Url, SearchParameterStatus Status)> value, JsonSerializer serializer)
        {
            if (value == null)
            {
                throw new JsonSerializationException("SearchParameterUrlStatuses must not be null.");
            }

            var statusesArray = new JArray();
            foreach (var (url, status) in value)
            {
                statusesArray.Add(new JObject { ["Url"] = url, ["Status"] = status.ToString(), });
            }

            statusesArray.WriteTo(writer);
        }
    }
}
