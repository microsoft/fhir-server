// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public delegate ResourceElement DeserializeFromString(RawResource rawResource, string versionId, DateTimeOffset? lastUpdated);

    public class ResourceDeserializer : IResourceDeserializer
    {
        /// <summary>
        /// A reusable memory stream manager for serializing and deserializing resources.
        /// </summary>
        internal static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

        private readonly IReadOnlyDictionary<FhirResourceFormat, DeserializeFromString> _deserializers;

        public ResourceDeserializer(
            IReadOnlyDictionary<FhirResourceFormat, DeserializeFromString> deserializers)
        {
            EnsureArg.IsNotNull(deserializers, nameof(deserializers));

            _deserializers = deserializers;
        }

        public ResourceDeserializer(params (FhirResourceFormat Format, DeserializeFromString Func)[] deserializers)
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

        public ResourceElement Deserialize(RawResourceElement rawResourceElement)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));

            ResourceElement resource = DeserializeRawResourceElement(rawResourceElement);

            return resource;
        }

        internal ResourceElement DeserializeRaw(RawResource rawResource, string version, DateTimeOffset lastModified)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            if (!_deserializers.TryGetValue(rawResource.Format, out var deserializer))
            {
                throw new NotSupportedException();
            }

            return deserializer(rawResource, version, lastModified);
        }

        internal ResourceElement DeserializeRawResourceElement(RawResourceElement rawResourceElement)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));

            if (!_deserializers.TryGetValue(rawResourceElement.Format, out var deserializer))
            {
                throw new NotSupportedException();
            }

            return deserializer(rawResourceElement.RawResource, rawResourceElement.VersionId, rawResourceElement.LastUpdated.HasValue ? rawResourceElement.LastUpdated.Value : DateTimeOffset.MinValue);
        }
    }
}
