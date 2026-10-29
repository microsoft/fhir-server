// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// A JSON file read from a FHIR NPM package.
    /// </summary>
    public sealed class FhirPackageEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageEntry"/> class.
        /// </summary>
        /// <param name="path">The validated archive path, for example <c>package/SearchParameter-example.json</c>.</param>
        /// <param name="resourceType">The declared <c>resourceType</c>, or <c>null</c> for non-resource JSON such as <c>.index.json</c>.</param>
        /// <param name="json">The original JSON text.</param>
        public FhirPackageEntry(string path, string resourceType, string json)
        {
            EnsureArg.IsNotNullOrWhiteSpace(path, nameof(path));
            EnsureArg.IsNotNull(json, nameof(json));

            Path = path;
            ResourceType = resourceType;
            Json = json;
        }

        /// <summary>
        /// Gets the validated archive path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the declared resource type, or <c>null</c> when the entry is not a FHIR resource.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the original JSON text.
        /// </summary>
        public string Json { get; }
    }
}
