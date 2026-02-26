// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Provides a mechanism to create a <see cref="RawResource"/>
    /// </summary>
    public class RawResourceFactory : IRawResourceFactory
    {
        private readonly IIgnixaJsonSerializer _ignixaJsonSerializer;
        private readonly FhirJsonSerializer _fhirJsonSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="RawResourceFactory"/> class.
        /// </summary>
        /// <param name="ignixaJsonSerializer">The Ignixa JSON serializer to use for serializing the resource.</param>
        /// <param name="fhirJsonSerializer">The Firely FhirJsonSerializer for backward compatibility with Firely-based ResourceElement.</param>
        public RawResourceFactory(IIgnixaJsonSerializer ignixaJsonSerializer, FhirJsonSerializer fhirJsonSerializer)
        {
            EnsureArg.IsNotNull(ignixaJsonSerializer, nameof(ignixaJsonSerializer));
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));

            _ignixaJsonSerializer = ignixaJsonSerializer;
            _fhirJsonSerializer = fhirJsonSerializer;
        }

        /// <inheritdoc />
        public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            // Try to get an Ignixa ResourceJsonNode from the underlying ITypedElement.
            // If the resource came from Ignixa parsing, we can serialize directly without POCO conversion.
            var ignixaNode = TryGetIgnixaResourceNode(resource);
            if (ignixaNode != null)
            {
                return CreateFromIgnixa(ignixaNode, keepMeta, keepVersion);
            }

            // Fall back to Firely-based serialization for resources from other sources
            return CreateFromFirely(resource, keepMeta, keepVersion);
        }

        /// <summary>
        /// Attempts to extract an Ignixa ResourceJsonNode from the ResourceElement.
        /// </summary>
        /// <remarks>
        /// When a ResourceElement is created from an IgnixaResourceElement via ToResourceElement(),
        /// the ResourceJsonNode is stored in the internal ResourceInstance property for efficient
        /// serialization without POCO conversion.
        /// </remarks>
        private static ResourceJsonNode TryGetIgnixaResourceNode(ResourceElement resource)
        {
            // Use the extension method to get the Ignixa node
            return resource.GetIgnixaNode();
        }

        /// <summary>
        /// Creates a RawResource directly from an Ignixa ResourceJsonNode.
        /// </summary>
        private RawResource CreateFromIgnixa(ResourceJsonNode resourceNode, bool keepMeta, bool keepVersion)
        {
            // Handle meta version according to the flags
            string originalVersionId = resourceNode.Meta?.VersionId;
            try
            {
                if (!keepMeta)
                {
                    if (resourceNode.Meta != null)
                    {
                        resourceNode.Meta.VersionId = null;
                    }
                }
                else if (!keepVersion && resourceNode.Meta != null)
                {
                    resourceNode.Meta.VersionId = "1";
                }

                string json = _ignixaJsonSerializer.Serialize(resourceNode);
                return new RawResource(json, FhirResourceFormat.Json, keepMeta);
            }
            finally
            {
                // Restore original versionId if we cleared it
                if (!keepMeta && resourceNode.Meta != null)
                {
                    resourceNode.Meta.VersionId = originalVersionId;
                }
            }
        }

        /// <summary>
        /// Creates a RawResource using Firely POCO serialization (legacy path).
        /// </summary>
        private RawResource CreateFromFirely(ResourceElement resource, bool keepMeta, bool keepVersion)
        {
            var poco = resource.ToPoco<Resource>();

            poco.Meta = poco.Meta ?? new Meta();
            var versionId = poco.Meta.VersionId;

            try
            {
                // Clear meta version if keepMeta is false since this is set based on generated values when saving the resource
                if (!keepMeta)
                {
                    poco.Meta.VersionId = null;
                }
                else if (!keepVersion)
                {
                    // Assume it's 1, though it may get changed by the database.
                    poco.Meta.VersionId = "1";
                }

                // Serialize using Firely, then re-parse and serialize with Ignixa for consistent output format
                string firelyJson = _fhirJsonSerializer.SerializeToString(poco);
                var resourceNode = _ignixaJsonSerializer.Parse(firelyJson);
                string json = _ignixaJsonSerializer.Serialize(resourceNode);

                return new RawResource(json, FhirResourceFormat.Json, keepMeta);
            }
            finally
            {
                if (!keepMeta)
                {
                    poco.Meta.VersionId = versionId;
                }
            }
        }
    }
}
