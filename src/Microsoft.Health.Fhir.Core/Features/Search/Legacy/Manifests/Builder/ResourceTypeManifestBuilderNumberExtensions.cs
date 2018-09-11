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
    internal static class ResourceTypeManifestBuilderNumberExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddNumberSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, int?> intSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(intSelector, nameof(intSelector));

            var extractor = new NumberExtractor<TResource>(
                resource => Enumerable.Repeat((decimal?)intSelector(resource), 1));

            return builder.AddNumberSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddNumberSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, int?> intSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(intSelector, nameof(intSelector));

            var extractor = new NumberExtractor<TResource>(
                resource => collectionSelector.ExtractNonEmptyCollection(resource).Select(c => (decimal?)intSelector(c)));

            return builder.AddNumberSearchParam(paramName, extractor);
        }
    }
}
