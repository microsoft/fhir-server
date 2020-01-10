// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Serialization
{
    internal class VersionSpecificReferenceConverter : JsonConverter
    {
        private IModelInfoProvider _modelInfoProvider;

        public VersionSpecificReferenceConverter(IModelInfoProvider modelInfoProvider)
        {
            _modelInfoProvider = modelInfoProvider;
        }

        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            EnsureArg.IsNotNull(writer, nameof(writer));
            EnsureArg.IsNotNull(serializer, nameof(serializer));

            if (value is ICanonicalObject obj)
            {
                if (_modelInfoProvider.Version.Equals(FhirSpecification.Stu3))
                {
                    serializer.Serialize(writer, new ReferenceComponent(obj.CanonicalObject.ToString()));
                }
                else
                {
                    serializer.Serialize(writer, obj.CanonicalObject);
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

            return objectType.IsGenericType &&
                   objectType.GetGenericTypeDefinition().IsAssignableFrom(typeof(CanonicalObjectHashSet<>));
        }
    }
}
