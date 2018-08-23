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
    internal static class ResourceTypeManifestBuilderUriExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddUriSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<string>> urisSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new UriExtractor<TResource>(
                urisSelector);

            return builder.AddUriSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddUriSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> uriSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(uriSelector, nameof(uriSelector));

            var extractor = new UriExtractor<TResource>(
                    resource => collectionSelector.ExtractNonEmptyCollection(resource)
                        .Select(uriSelector));

            return builder.AddUriSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddUriSearchParam<TResource>(
           this ResourceTypeManifestBuilder<TResource> builder,
           string paramName,
           Func<TResource, string> uriSelector)
           where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new UriExtractor<TResource>(
                resource => Enumerable.Repeat(uriSelector(resource), 1));

            return builder.AddUriSearchParam(paramName, extractor);
        }
    }
}
