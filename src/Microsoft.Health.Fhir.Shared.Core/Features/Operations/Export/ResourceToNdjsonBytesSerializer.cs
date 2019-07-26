// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// A serializer used to serialize the resource represented by <see cref="ResourceWrapper"/> to byte array representing new line deliminated JSON.
    /// </summary>
    public class ResourceToNdjsonBytesSerializer : IResourceToByteArraySerializer
    {
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly FhirJsonSerializer _fhirJsonSerializer;

        public ResourceToNdjsonBytesSerializer(ResourceDeserializer resourceDeserializer, FhirJsonSerializer fhirJsonSerializer)
        {
            EnsureArg.IsNotNull(resourceDeserializer, nameof(resourceDeserializer));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));

            _resourceDeserializer = resourceDeserializer;
            _fhirJsonSerializer = fhirJsonSerializer;
        }

        /// <inheritdoc />
        public byte[] Serialize(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            ResourceElement resource = _resourceDeserializer.DeserializeRaw(resourceWrapper.RawResource, resourceWrapper.Version, resourceWrapper.LastModified);

            string resourceData = _fhirJsonSerializer.SerializeToString(resource.ToPoco<Resource>());

            byte[] bytesToWrite = Encoding.UTF8.GetBytes($"{resourceData}\n");

            return bytesToWrite;
        }
    }
}
