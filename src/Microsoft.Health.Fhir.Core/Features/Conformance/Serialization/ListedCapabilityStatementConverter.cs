// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using EnumLiteralAttribute = Hl7.Fhir.Utility.EnumLiteralAttribute;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    internal class ListedCapabilityStatementConverter : JsonConverter
    {
        public override bool CanRead
        {
            get { return false; }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JToken t = JToken.FromObject(value);

            if (t.Type != JTokenType.Object && t.Type != JTokenType.Array)
            {
                t.WriteTo(writer);
            }
            else if (t.Type == JTokenType.Array)
            {
                writer.WriteStartArray();

                foreach (object item in (IEnumerable)value)
                {
                    serializer.Serialize(writer, item);
                }

                writer.WriteEndArray();
            }
            else
            {
                writer.WriteStartObject();

                foreach (PropertyInfo prop in value.GetType().GetProperties().Where(x => x.CanRead))
                {
                    object obj = prop.GetValue(value);

                    if (obj == null)
                    {
                        continue;
                    }

                    var resolver = serializer.ContractResolver.ResolveContract(prop.DeclaringType) as JsonObjectContract;
                    var propName = resolver?.Properties.Where(x => x.UnderlyingName == prop.Name).Select(x => x.PropertyName).FirstOrDefault()
                                   ?? prop.Name;

                    writer.WritePropertyName(propName);

                    SelectSingleAttribute defaultCap = prop.GetCustomAttributes(false)?.OfType<SelectSingleAttribute>().FirstOrDefault();

                    JToken tokenValue = JToken.FromObject(obj);

                    // Write single default value
                    if (defaultCap != null)
                    {
                        serializer.Serialize(writer, defaultCap.DefaultValue);
                    }
                    else
                    {
                        if (tokenValue.Type != JTokenType.Object && tokenValue.Type != JTokenType.Array)
                        {
                            if (obj is Enum)
                            {
                                FieldInfo field = obj.GetType().GetField(obj.ToString());
                                EnumLiteralAttribute attr = field.GetCustomAttributes().OfType<EnumLiteralAttribute>().FirstOrDefault();

                                serializer.Serialize(writer, attr?.Literal ?? obj?.ToString());
                            }
                            else
                            {
                                tokenValue.WriteTo(writer);
                            }
                        }
                        else
                        {
                            serializer.Serialize(writer, obj);
                        }
                    }
                }

                writer.WriteEndObject();
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException("Unnecessary because CanRead is false. The type will skip the converter.");
        }

        public override bool CanConvert(Type objectType)
        {
            return true;
        }
    }
}
