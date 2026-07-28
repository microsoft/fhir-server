// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Text selected for vector indexing together with its FHIR provenance.
    /// </summary>
    public sealed class VectorTextSource
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorTextSource"/> class.
        /// </summary>
        /// <param name="text">The source text.</param>
        /// <param name="resourceType">The source resource type.</param>
        /// <param name="resourceId">The source resource id.</param>
        /// <param name="resourceVersion">The source resource version.</param>
        /// <param name="path">The source element path.</param>
        public VectorTextSource(string text, string resourceType, string resourceId, string resourceVersion, string path)
        {
            Text = EnsureArg.IsNotNull(text, nameof(text));
            ResourceType = EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            ResourceId = EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));
            ResourceVersion = resourceVersion;
            Path = EnsureArg.IsNotNullOrWhiteSpace(path, nameof(path));
        }

        /// <summary>
        /// Gets the source text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the source resource type.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the source resource id.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Gets the source resource version.
        /// </summary>
        public string ResourceVersion { get; }

        /// <summary>
        /// Gets the source element path.
        /// </summary>
        public string Path { get; }
    }
}
