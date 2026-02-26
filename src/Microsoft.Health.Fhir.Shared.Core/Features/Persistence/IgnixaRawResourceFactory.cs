// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Creates <see cref="RawResource"/> instances from Ignixa's <see cref="ResourceJsonNode"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This factory provides high-performance serialization by working directly with Ignixa's
    /// mutable JSON document model, avoiding conversion to/from Firely POCOs.
    /// </para>
    /// <para>
    /// The factory handles meta.versionId manipulation similar to <see cref="RawResourceFactory"/>
    /// but operates on the JSON structure directly.
    /// </para>
    /// </remarks>
    public class IgnixaRawResourceFactory : IIgnixaRawResourceFactory
    {
        private readonly IIgnixaJsonSerializer _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaRawResourceFactory"/> class.
        /// </summary>
        /// <param name="serializer">The Ignixa JSON serializer for resource serialization.</param>
        public IgnixaRawResourceFactory(IIgnixaJsonSerializer serializer)
        {
            EnsureArg.IsNotNull(serializer, nameof(serializer));

            _serializer = serializer;
        }

        /// <inheritdoc />
        public RawResource Create(ResourceJsonNode resource, bool keepMeta, bool keepVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            // Store original versionId if we need to restore it
            string originalVersionId = resource.Meta.VersionId;

            try
            {
                // Clear meta version if keepMeta is false since this is set based on generated values when saving the resource
                if (!keepMeta)
                {
                    resource.Meta.VersionId = null;
                }
                else if (!keepVersion)
                {
                    // Assume it's 1, though it may get changed by the database.
                    resource.Meta.VersionId = "1";
                }

                string json = _serializer.Serialize(resource);
                return new RawResource(json, FhirResourceFormat.Json, keepMeta);
            }
            finally
            {
                // Restore original version to avoid side effects on the input resource
                if (!keepMeta)
                {
                    resource.Meta.VersionId = originalVersionId;
                }
            }
        }
    }
}
