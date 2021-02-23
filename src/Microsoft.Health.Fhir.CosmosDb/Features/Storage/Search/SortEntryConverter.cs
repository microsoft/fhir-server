// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search
{
    public class SortEntryConverter : JsonConverter
    {
        private SortValueJObjectGenerator _generator = new SortValueJObjectGenerator();

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(SortValue);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // We don't currently support reading the search index from the Cosmos DB.
            reader.Skip();

            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var searchIndexEntry = (SortValue)value;
            _generator.Generate(searchIndexEntry).WriteTo(writer);
        }
    }
}
