// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    internal class ProfileReferenceConverter : JsonConverter
    {
        private IModelInfoProvider _modelInfoProvider;

        public ProfileReferenceConverter(IModelInfoProvider modelInfoProvider)
        {
            _modelInfoProvider = modelInfoProvider;
        }

        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            EnsureArg.IsNotNull(writer, nameof(writer));
            EnsureArg.IsNotNull(serializer, nameof(serializer));

            if (value is ReferenceComponent obj)
            {
                if (_modelInfoProvider.Version.Equals(FhirSpecification.Stu3))
                {
                    serializer = new JsonSerializer()
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver(),
                    };
                    var token = JToken.FromObject(obj, serializer);

                    token.WriteTo(writer);
                }
                else
                {
                    serializer.Serialize(writer, obj.Reference);
                }
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            EnsureArg.IsNotNull(objectType, nameof(objectType));

            return typeof(ReferenceComponent).IsAssignableFrom(objectType);
        }
    }
}
