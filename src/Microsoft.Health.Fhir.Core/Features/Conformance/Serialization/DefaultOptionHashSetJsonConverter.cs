// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    internal class DefaultOptionHashSetJsonConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            EnsureArg.IsNotNull(writer, nameof(writer));
            EnsureArg.IsNotNull(value, nameof(value));
            EnsureArg.IsNotNull(serializer, nameof(serializer));

            object defaultOption = null;

            if (value is IDefaultOption hasDefaultOption)
            {
                defaultOption = hasDefaultOption.DefaultOption;

                if (value is IEnumerable enumerable)
                {
                    List<object> objects = enumerable.Cast<object>().ToList();

                    // When a list is specified, check that the default is an option
                    if (objects.Any() && !objects.Any(x => Equals(defaultOption, x)))
                    {
                        throw new UnsupportedConfigurationException(string.Format(Core.Resources.InvalidConfigSetting, defaultOption, string.Join(", ", objects.ToArray())));
                    }
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
