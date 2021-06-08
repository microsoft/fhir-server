// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    /// <summary>
    /// Converts <see cref="Coding"/> into a simple serializable model for <see cref="CapabilityStatementBuilder"/>
    /// </summary>
    internal class CodingJsonConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            EnsureArg.IsNotNull(writer, nameof(writer));
            EnsureArg.IsNotNull(serializer, nameof(serializer));

            if (value is Coding obj)
            {
                var newObj = new CodingInfo
                {
                    Coding = obj.Code,
                    System = obj.System,
                    DisplayName = obj.Display,
                };

                serializer.Serialize(writer, newObj);
            }
            else
            {
                serializer.Serialize(writer, value);
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            EnsureArg.IsNotNull(objectType, nameof(objectType));

            return typeof(Coding).IsAssignableFrom(objectType);
        }

        internal class CodingInfo
        {
            [JsonProperty("code")]
            public string Coding { get; set; }

            [JsonProperty("system")]
            public string System { get; set; }

            [JsonProperty("display")]
            public string DisplayName { get; set; }
        }
    }
}
