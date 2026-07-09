// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Sdk;
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
        private readonly ISdkModeProvider _sdkModeProvider;
        private readonly ISdkFallbackGuard _fallbackGuard;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceToNdjsonBytesSerializer"/> class.
        /// </summary>
        /// <param name="ignixaSerializer">The Ignixa JSON serializer for FHIR resources.</param>
        /// <param name="sdkModeProvider">The active SDK mode provider.</param>
        /// <param name="fallbackGuard">The SDK fallback guard.</param>
        public ResourceToNdjsonBytesSerializer(
            IIgnixaJsonSerializer ignixaSerializer,
            ISdkModeProvider sdkModeProvider = null,
            ISdkFallbackGuard fallbackGuard = null)
        {
            if (sdkModeProvider?.IsFirelyMode != true)
            {
                EnsureArg.IsNotNull(ignixaSerializer, nameof(ignixaSerializer));
            }

            _ignixaSerializer = ignixaSerializer;
            _sdkModeProvider = sdkModeProvider;
            _fallbackGuard = fallbackGuard;
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
            if (_sdkModeProvider?.IsFirelyMode == true)
            {
                return SerializeWithFirely(resourceElement);
            }

            // OPTIMIZED: Direct Ignixa serialization (no round-trip through Firely)
            // This is now safe after PR #165 fixed Ignixa ↔ Firely compatibility
            var ignixaNode = resourceElement.GetIgnixaNode();
            if (ignixaNode != null)
            {
                // Fast path: Direct serialization from Ignixa node (most common case)
                return _ignixaSerializer.Serialize(ignixaNode, pretty: false);
            }

            if (_sdkModeProvider?.IsIgnixaMode == true)
            {
                throw new InvalidOperationException("Ignixa SDK mode requires export resources to be backed by an Ignixa ResourceJsonNode.");
            }

            _fallbackGuard?.FirelyFallback(
                "Export NDJSON serialization",
                "ResourceElement was not backed by an Ignixa ResourceJsonNode.");

            return SerializeWithFirely(resourceElement);
        }

        private static string SerializeWithFirely(ResourceElement resourceElement)
        {
            // Legacy fallback: For Firely-based ResourceElements (shouldn't happen post-migration)
            // Return Firely JSON directly without attempting to parse/reserialize through Ignixa
            return resourceElement.Instance.ToJson();
        }
    }
}
