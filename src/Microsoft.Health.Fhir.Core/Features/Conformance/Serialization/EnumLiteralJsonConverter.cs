// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Utility;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    internal class EnumLiteralJsonConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            EnsureArg.IsNotNull(writer, nameof(writer));
            EnsureArg.IsNotNull(serializer, nameof(serializer));

            if (value is Enum obj)
            {
                // If an enum has multiple values that map to the primitive value the GetLiteral method will fail
                // with a key conflict as it tries to make a bidirectinoal mapping dictionary.
                // The catch allows for a more simplistic mapping of an enum value to a string.
                string enumString;
                try
                {
                    enumString = obj.GetLiteral();
                }
                catch
                {
                    enumString = obj.ToString();
                }

                serializer.Serialize(writer, enumString);
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            EnsureArg.IsNotNull(objectType, nameof(objectType));

            return objectType.IsEnum;
        }
    }
}
