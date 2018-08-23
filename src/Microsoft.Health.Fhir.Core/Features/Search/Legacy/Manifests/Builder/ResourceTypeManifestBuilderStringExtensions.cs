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
    internal static class ResourceTypeManifestBuilderStringExtensions
    {
        internal static ResourceTypeManifestBuilder<TResource> AddStringSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<string>> stringsSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            var extractor = new StringExtractor<TResource>(stringsSelector);

            return builder.AddStringSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddStringSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, string> stringSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(stringSelector, nameof(stringSelector));

            var extractor = new StringExtractor<TResource>(
                resource => Enumerable.Repeat(stringSelector(resource), 1));

            return builder.AddStringSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddStringSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, FhirString> stringSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(stringSelector, nameof(stringSelector));

            var extractor = new StringExtractor<TResource>(
                resource => Enumerable.Repeat(stringSelector(resource)?.Value, 1));

            return builder.AddStringSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddStringSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, IEnumerable<string>> stringSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(stringSelector, nameof(stringSelector));

            var extractor = new StringExtractor<TResource>(
                resource => collectionSelector.ExtractNonEmptyCollection(resource)
                    .SelectMany(stringSelector));

            return builder.AddStringSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddStringSearchParam<TResource, TCollection>(
            this ResourceTypeManifestBuilder<TResource> builder,
            string paramName,
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            Func<TCollection, string> stringSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));
            EnsureArg.IsNotNull(stringSelector, nameof(stringSelector));

            var extractor = new StringExtractor<TResource>(
                resource => collectionSelector.ExtractNonEmptyCollection(resource)
                    .Select(stringSelector));

            return builder.AddStringSearchParam(paramName, extractor);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddNameSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            Func<TResource, IEnumerable<HumanName>> collectionSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));

            // https://www.hl7.org/fhir/patient.html recommends the following:
            // A server defined search that may match any of the string fields in the HumanName, including family, give, prefix, suffix, suffix, and/or text
            // we will do a basic search based on family or given or prefix or suffix or text for now. Details on localization will be handled later.
            return builder.AddStringSearchParam(SearchParamNames.Name, collectionSelector, n => n.Given)
                .AddStringSearchParam(SearchParamNames.Name, collectionSelector, n => n.Family)
                .AddStringSearchParam(SearchParamNames.Name, collectionSelector, n => n.Prefix)
                .AddStringSearchParam(SearchParamNames.Name, collectionSelector, n => n.Suffix)
                .AddStringSearchParam(SearchParamNames.Name, collectionSelector, n => n.Text);
        }

        internal static ResourceTypeManifestBuilder<TResource> AddAddressSearchParam<TResource>(
            this ResourceTypeManifestBuilder<TResource> builder,
            Func<TResource, IEnumerable<Address>> collectionSelector)
            where TResource : Resource
        {
            EnsureArg.IsNotNull(builder, nameof(builder));
            EnsureArg.IsNotNull(collectionSelector, nameof(collectionSelector));

            // https://www.hl7.org/fhir/patient.html recommends the following:
            // A server defined search that may match any of the string fields in the Address, including line, city, state, country, postalCode, and/or text
            // Details on localization will be handled later if needed.
            return builder.AddStringSearchParam(SearchParamNames.Address, collectionSelector, a => a.Line)
                .AddStringSearchParam(SearchParamNames.Address, collectionSelector, a => a.City)
                .AddStringSearchParam(SearchParamNames.Address, collectionSelector, a => a.District)
                .AddStringSearchParam(SearchParamNames.Address, collectionSelector, a => a.State)
                .AddStringSearchParam(SearchParamNames.Address, collectionSelector, a => a.Country)
                .AddStringSearchParam(SearchParamNames.Address, collectionSelector, a => a.PostalCode)
                .AddStringSearchParam(SearchParamNames.Address, collectionSelector, a => a.Text);
        }
    }
}
