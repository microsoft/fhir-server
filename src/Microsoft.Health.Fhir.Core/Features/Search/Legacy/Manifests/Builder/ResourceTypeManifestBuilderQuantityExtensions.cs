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
    internal static class ResourceTypeManifestBuilderQuantityExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddQuantitySearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, Quantity> quantitySelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new QuantityExtractor<TResource, TCollection>(
                collectionSelector,
                quantitySelector);

            return builder.AddQuantitySearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddQuantitySearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<Quantity>> quantitiesSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new QuantityExtractor<TResource, Quantity>(
                quantitiesSelector,
                quantity => quantity);

            return builder.AddQuantitySearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddQuantitySearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, Quantity> quantitySelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new QuantityExtractor<TResource, TResource>(
                resource => Enumerable.Repeat(resource, 1),
                resource => quantitySelector(resource));

            return builder.AddQuantitySearchParam(paramName, extractor);
        }
    }
}
