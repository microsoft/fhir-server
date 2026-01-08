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
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// A serializer used to serialize the resource represented by <see cref="ResourceWrapper"/> to byte array representing new line deliminated JSON.
    /// </summary>
    public class ResourceToNdjsonBytesSerializer : IResourceToByteArraySerializer
    {
        private readonly IIgnixaJsonSerializer _ignixaSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceToNdjsonBytesSerializer"/> class.
        /// </summary>
        /// <param name="ignixaSerializer">The Ignixa JSON serializer for FHIR resources.</param>
        public ResourceToNdjsonBytesSerializer(IIgnixaJsonSerializer ignixaSerializer)
        {
            EnsureArg.IsNotNull(ignixaSerializer, nameof(ignixaSerializer));

            _ignixaSerializer = ignixaSerializer;
        }

        /// <inheritdoc />
        public byte[] Serialize(ResourceElement resourceElement)
        {
            EnsureArg.IsNotNull(resourceElement, nameof(resourceElement));

            string resourceData = SerializeToJson(resourceElement);

            byte[] bytesToWrite = Encoding.UTF8.GetBytes($"{resourceData}\n");

            return bytesToWrite;
        }

        public string StringSerialize(ResourceElement resourceElement, bool addSoftDeletedExtension = false)
        {
            EnsureArg.IsNotNull(resourceElement, nameof(resourceElement));

            if (addSoftDeletedExtension)
            {
                resourceElement = resourceElement.TryAddSoftDeletedExtension();
            }

            return SerializeToJson(resourceElement);
        }

        private string SerializeToJson(ResourceElement resourceElement)
        {
            // OPTIMIZED: Direct Ignixa serialization (no round-trip through Firely)
            // This is now safe after PR #165 fixed Ignixa ↔ Firely compatibility
            var ignixaNode = resourceElement.GetIgnixaNode();
            if (ignixaNode != null)
            {
                // Fast path: Direct serialization from Ignixa node (most common case)
                return _ignixaSerializer.Serialize(ignixaNode, pretty: false);
            }

            // Legacy fallback: For Firely-based ResourceElements (shouldn't happen post-migration)
            // Convert Firely ITypedElement → JSON → Ignixa → JSON
            string firelyJson = resourceElement.Instance.ToJson();
            var resourceNode = _ignixaSerializer.Parse(firelyJson);
            return _ignixaSerializer.Serialize(resourceNode, pretty: false);
        }
    }
}
