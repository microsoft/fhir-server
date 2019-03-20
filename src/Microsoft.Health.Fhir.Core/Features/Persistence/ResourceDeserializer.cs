// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class ResourceDeserializer
    {
        private readonly IReadOnlyDictionary<ResourceFormat, Func<string, Resource>> _deserializers;

        public ResourceDeserializer(IReadOnlyDictionary<ResourceFormat, Func<string, Resource>> deserializers)
        {
            EnsureArg.IsNotNull(deserializers, nameof(deserializers));

            _deserializers = deserializers;
        }

        public ResourceDeserializer(params (ResourceFormat Format, Func<string, Resource> Func)[] deserializers)
        {
            EnsureArg.IsNotNull(deserializers, nameof(deserializers));

            _deserializers = deserializers.ToDictionary(x => x.Format, x => x.Func);
        }

        public Resource Deserialize(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            Resource resource = DeserializeRaw(resourceWrapper.RawResource);

            resource.VersionId = resourceWrapper.Version;
            resource.Meta.LastUpdated = resourceWrapper.LastModified;

            return resource;
        }

        internal Resource DeserializeRaw(RawResource rawResource)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            if (!_deserializers.TryGetValue(rawResource.Format, out var deserializer))
            {
                throw new NotSupportedException();
            }

            return deserializer(rawResource.Data);
        }
    }
}
