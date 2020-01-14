// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
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

            if (value is IReferenceComponent obj)
            {
                if (_modelInfoProvider.Version.Equals(FhirSpecification.Stu3))
                {
                    serializer.Serialize(writer, obj.ReferenceComponent);
                }
                else
                {
                    serializer.Serialize(writer, obj.ReferenceComponent.Reference);
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

            return typeof(IReferenceComponent).IsAssignableFrom(objectType);
        }
    }
}
