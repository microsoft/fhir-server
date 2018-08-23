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
    internal static class ResourceTypeManifestBuilderDateTimeExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> dateTimeStartSelector,
            Func<TCollection, string> dateTimeEndSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new DateTimeExtractor<TResource, TCollection>(
                collectionSelector,
                dateTimeStartSelector,
                dateTimeEndSelector);

            return builder.AddDateTimeSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, string> dateSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(dateSelector, nameof(dateSelector));

            var extractor = new DateTimeExtractor<TResource, string>(
                resource => Enumerable.Repeat(dateSelector(resource), 1),
                s => s,
                e => e);

            return builder.AddDateTimeSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, Date> dateSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(dateSelector, nameof(dateSelector));

            var extractor = new DateTimeExtractor<TResource, Date>(
                resource => Enumerable.Repeat(dateSelector(resource), 1),
                s => s.Value,
                e => e.Value);

            return builder.AddDateTimeSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, FhirDateTime> dateTimeSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(dateTimeSelector, nameof(dateTimeSelector));

            var extractor = new DateTimeExtractor<TResource, FhirDateTime>(
                resource => collectionSelector.ExtractNonEmptyCollection(resource).Select(dateTimeSelector),
                s => s.Value,
                e => e.Value);

            return builder.AddDateTimeSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, FhirDateTime> dateTimeSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(dateTimeSelector, nameof(dateTimeSelector));

            var extractor = new DateTimeExtractor<TResource, FhirDateTime>(
                resource => Enumerable.Repeat(dateTimeSelector(resource), 1),
                s => s.Value,
                e => e.Value);

            return builder.AddDateTimeSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, Period> periodTimeSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(periodTimeSelector, nameof(periodTimeSelector));

            var extractor = new DateTimeExtractor<TResource, Period>(
                resource => Enumerable.Repeat(periodTimeSelector(resource), 1),
                s => s.Start,
                e => e.End);

            return builder.AddDateTimeSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, Instant> instantSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(instantSelector, nameof(instantSelector));

            var extractor = new DateTimeExtractor<TResource, Instant>(
                resource => Enumerable.Repeat(instantSelector(resource), 1),
                s => s.ToString(),
                e => e.ToString());

            return builder.AddDateTimeSearchParam(paramName, extractor);
        }
    }
}
