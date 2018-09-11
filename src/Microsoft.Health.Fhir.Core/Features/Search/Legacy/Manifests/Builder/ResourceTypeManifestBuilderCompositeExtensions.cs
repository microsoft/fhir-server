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
    internal static class ResourceTypeManifestBuilderCompositeExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddCompositeDateTimeSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, CodeableConcept> compositeTokenSelector,
            Func<TResource, FhirDateTime> dateSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new CompositeDateTimeExtractor<TResource>(
                compositeTokenSelector,
                dateSelector);

            return builder.AddCompositeDateTimeSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddCompositeQuantitySearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, CodeableConcept> compositeTokenSelector,
            Func<TCollection, Quantity> quantitySelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new CompositeQuantityExtractor<TResource, TCollection>(
                collectionSelector,
                compositeTokenSelector,
                quantitySelector);

            return builder.AddCompositeQuantitySearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddCompositeQuantitySearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, CodeableConcept> compositeTokenSelector,
            Func<TResource, Quantity> quantitySelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new CompositeQuantityExtractor<TResource, TResource>(
                resource => Enumerable.Repeat(resource, 1),
                compositeTokenSelector,
                quantitySelector);

            return builder.AddCompositeQuantitySearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddCompositeReferenceSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, Enum> enumSelector,
            Func<TCollection, ResourceReference> referenceSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new CompositeReferenceExtractor<TResource, TCollection>(
                collectionSelector,
                enumSelector,
                referenceSelector);

            return builder.AddCompositeReferenceSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddCompositeStringSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, CodeableConcept> compositeTokenSelector,
            Func<TResource, FhirString> stringSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new CompositeStringExtractor<TResource>(
                compositeTokenSelector,
                stringSelector);

            return builder.AddCompositeStringSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddCompositeTokenSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, CodeableConcept> compositeTokenSelector,
            Func<TCollection, CodeableConcept> tokenSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new CompositeTokenExtractor<TResource, TCollection>(
                collectionSelector,
                compositeTokenSelector,
                tokenSelector);

            return builder.AddCompositeTokenSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddCompositeTokenSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string parameter,
            Func<TResource, CodeableConcept> compositeTokenSelector,
            Func<TResource, CodeableConcept> tokenSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new CompositeTokenExtractor<TResource, TResource>(
                resource => Enumerable.Repeat(resource, 1),
                compositeTokenSelector,
                tokenSelector);

            return builder.AddCompositeTokenSearchParam(parameter, extractor);
        }
    }
}
