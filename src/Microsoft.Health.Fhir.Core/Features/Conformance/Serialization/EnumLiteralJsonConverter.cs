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
