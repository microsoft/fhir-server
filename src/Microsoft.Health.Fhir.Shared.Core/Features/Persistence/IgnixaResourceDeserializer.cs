// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Deserializes stored FHIR resources into Ignixa's <see cref="IgnixaResourceElement"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deserializer provides high-performance deserialization by parsing JSON directly
    /// into Ignixa's mutable document model, avoiding intermediate POCO allocations.
    /// </para>
    /// <para>
    /// The deserializer updates meta.versionId and meta.lastUpdated on the resulting element
    /// to ensure consistency with the stored resource metadata.
    /// </para>
    /// </remarks>
    public class IgnixaResourceDeserializer : IIgnixaResourceDeserializer
    {
        private readonly IIgnixaJsonSerializer _serializer;
        private readonly IIgnixaSchemaContext _schemaContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaResourceDeserializer"/> class.
        /// </summary>
        /// <param name="serializer">The Ignixa JSON serializer for parsing resources.</param>
        /// <param name="schemaContext">The schema context providing FHIR version metadata.</param>
        public IgnixaResourceDeserializer(IIgnixaJsonSerializer serializer, IIgnixaSchemaContext schemaContext)
        {
            EnsureArg.IsNotNull(serializer, nameof(serializer));
            EnsureArg.IsNotNull(schemaContext, nameof(schemaContext));

            _serializer = serializer;
            _schemaContext = schemaContext;
        }

        /// <inheritdoc />
        public IgnixaResourceElement Deserialize(ResourceWrapper resourceWrapper)
        {
            EnsureArg.IsNotNull(resourceWrapper, nameof(resourceWrapper));

            return Deserialize(
                resourceWrapper.RawResource,
                resourceWrapper.Version,
                resourceWrapper.LastModified);
        }

        /// <inheritdoc />
        public IgnixaResourceElement Deserialize(RawResourceElement rawResourceElement)
        {
            EnsureArg.IsNotNull(rawResourceElement, nameof(rawResourceElement));

            return Deserialize(
                rawResourceElement.RawResource.Data,
                rawResourceElement.VersionId,
                rawResourceElement.LastUpdated ?? DateTimeOffset.MinValue);
        }

        /// <inheritdoc />
        public IgnixaResourceElement Deserialize(string json, string version, DateTimeOffset lastModified)
        {
            EnsureArg.IsNotNullOrWhiteSpace(json, nameof(json));

            var resourceNode = _serializer.Parse(json);
            var element = new IgnixaResourceElement(resourceNode, _schemaContext.Schema);

            // Update meta with version and lastUpdated from the wrapper
            // This ensures consistency even if the stored JSON has outdated meta
            if (!string.IsNullOrEmpty(version))
            {
                element.SetVersionId(version);
            }

            if (lastModified != DateTimeOffset.MinValue)
            {
                element.SetLastUpdated(lastModified);
            }

            return element;
        }

        /// <summary>
        /// Deserializes a <see cref="RawResource"/> with version and timestamp metadata.
        /// </summary>
        /// <param name="rawResource">The raw resource containing JSON data.</param>
        /// <param name="version">The version identifier to set on the resource's meta.</param>
        /// <param name="lastModified">The last modified timestamp to set on the resource's meta.</param>
        /// <returns>An <see cref="IgnixaResourceElement"/> with the specified meta information.</returns>
        /// <exception cref="NotSupportedException">
        /// Thrown when the resource format is not JSON.
        /// </exception>
        private IgnixaResourceElement Deserialize(RawResource rawResource, string version, DateTimeOffset lastModified)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));

            if (rawResource.Format != FhirResourceFormat.Json)
            {
                throw new NotSupportedException(
                    $"Ignixa deserialization only supports JSON format. Received: {rawResource.Format}");
            }

            return Deserialize(rawResource.Data, version, lastModified);
        }
    }
}
