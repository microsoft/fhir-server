// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceDeserializer
    {
        private readonly IReadOnlyDictionary<FhirResourceFormat, Func<string, string, DateTimeOffset, ResourceElement>> _deserializers;

        public ResourceDeserializer(IReadOnlyDictionary<FhirResourceFormat, Func<string, string, DateTimeOffset, ResourceElement>> deserializers)
        {
            EnsureArg.IsNotNull(deserializers, nameof(deserializers));

            _deserializers = deserializers;
        }

        public ResourceDeserializer(params (FhirResourceFormat Format, Func<string, string, DateTimeOffset, ResourceElement> Func)[] deserializers)
        {
            EnsureArg.IsNotNull(deserializers, nameof(deserializers));

            _deserializers = deserializers.ToDictionary(x => x.Format, x => x.Func);
        }

        public ResourceElement Deserialize(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            ResourceElement resource = DeserializeRaw(resourceWrapper.RawResource, resourceWrapper.Version, resourceWrapper.LastModified);

            return resource;
        }

        internal ResourceElement DeserializeRaw(RawResource rawResource, string version, DateTimeOffset lastModified)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            if (!_deserializers.TryGetValue(rawResource.Format, out var deserializer))
            {
                throw new NotSupportedException();
            }

            return deserializer(rawResource.Data, version, lastModified);
        }
    }
}
