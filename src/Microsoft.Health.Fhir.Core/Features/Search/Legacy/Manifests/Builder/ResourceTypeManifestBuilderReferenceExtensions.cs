// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    internal static class ResourceTypeManifestBuilderReferenceExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddReferenceSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, ResourceReference> referenceSelector,
            FHIRAllTypes? resourceTypeFilter = null)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new ReferenceExtractor<TResource, TCollection>(
                collectionSelector,
                referenceSelector,
                resourceTypeFilter);

            return builder.AddReferenceSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddReferenceSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<ResourceReference>> referencesSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(referencesSelector, nameof(referencesSelector));

            var extractor = new ReferenceExtractor<TResource, ResourceReference>(
                resource => referencesSelector(resource),
                reference => reference);

            return builder.AddReferenceSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddReferenceSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, ResourceReference> referenceSelector,
            FHIRAllTypes? resourceTypeFilter = null)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(referenceSelector, nameof(referenceSelector));

            var extractor = new ReferenceExtractor<TResource, ResourceReference>(
                resource =>
                {
                    return Enumerable.Repeat(referenceSelector(resource), 1)
                        .Where(r => r != null);
                },
                reference => reference,
                resourceTypeFilter);

            return builder.AddReferenceSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddReferenceSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, IEnumerable<ResourceReference>> resourceReferencesSelector,
            FHIRAllTypes? resourceTypeFilter = null)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(resourceReferencesSelector, nameof(resourceReferencesSelector));

            var extractor = new ReferenceExtractor<TResource, ResourceReference>(
                resource =>
                {
                    return collectionSelector.ExtractNonEmptyCollection(resource)
                        .SelectMany(resourceReferencesSelector)
                        .Where(reference => reference != null && !reference.IsEmpty());
                },
                reference => reference,
                resourceTypeFilter);

            return builder.AddReferenceSearchParam(paramName, extractor);
        }
    }
}
