// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Creates <see cref="RawResource"/> instances using Ignixa serialization only.
    /// </summary>
    public class IgnixaModeRawResourceFactory : IRawResourceFactory
    {
        private readonly IIgnixaJsonSerializer _ignixaJsonSerializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaModeRawResourceFactory"/> class.
        /// </summary>
        /// <param name="ignixaJsonSerializer">The Ignixa serializer used to write the resource.</param>
        public IgnixaModeRawResourceFactory(IIgnixaJsonSerializer ignixaJsonSerializer)
        {
            _ignixaJsonSerializer = EnsureArg.IsNotNull(ignixaJsonSerializer, nameof(ignixaJsonSerializer));
        }

        /// <inheritdoc />
        public RawResource Create(ResourceElement resource, bool keepMeta, bool keepVersion = false)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            ResourceJsonNode resourceNode = resource.GetIgnixaNode();
            if (resourceNode == null)
            {
                throw new InvalidOperationException("Ignixa mode requires a ResourceElement backed by an Ignixa resource node.");
            }

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
                if (!keepMeta && resourceNode.Meta != null)
                {
                    resourceNode.Meta.VersionId = originalVersionId;
                }
            }
        }
    }
}
