// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    internal class DefaultOptionHashSetJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            EnsureArg.IsNotNull(writer, nameof(writer));
            EnsureArg.IsNotNull(value, nameof(value));
            EnsureArg.IsNotNull(serializer, nameof(serializer));

            dynamic defaultOption = ((dynamic)value).DefaultOption;

            if (value is IEnumerable enumerable)
            {
                var exists = false;
                object first = null;

                foreach (object item in enumerable)
                {
                    if (first == null)
                    {
                        first = item;
                    }

                    if (defaultOption == item)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    defaultOption = first;
                }
            }

            serializer.Serialize(writer, defaultOption);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            EnsureArg.IsNotNull(objectType, nameof(objectType));

            return objectType.IsGenericType &&
                   objectType.GetGenericTypeDefinition().IsAssignableFrom(typeof(DefaultOptionHashSet<>));
        }
    }
}
