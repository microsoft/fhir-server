// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues
{
    /// <summary>
    /// The builder class to create an instance of <see cref="ResourceTypeManifest"/>.
    /// </summary>
    /// <typeparam name="TResource">The resource type of the manifest to create.</typeparam>
    internal sealed partial class ResourceTypeManifestBuilder<TResource>
        where TResource : Resource
    {
        private readonly ISearchParamFactory _searchParamFactory;

        private Dictionary<string, SearchParam> _searchParams = new Dictionary<string, SearchParam>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceTypeManifestBuilder{TResource}"/> class.
        /// </summary>
        /// <param name="searchParamFactory">The factory to create an instance of <see cref="SearchParam"/>.</param>
        internal ResourceTypeManifestBuilder(ISearchParamFactory searchParamFactory)
        {
            EnsureArg.IsNotNull(searchParamFactory, nameof(searchParamFactory));

            _searchParamFactory = searchParamFactory;
        }

        /// <summary>
        /// Returns the created <see cref="ResourceTypeManifest"/> instance.
        /// </summary>
        /// <returns>The created <see cref="ResourceTypeManifest"/> instance.</returns>
        internal ResourceTypeManifest ToManifest()
        {
            return new ResourceTypeManifest(typeof(TResource), _searchParams.Values);
        }

        internal ResourceTypeManifestBuilder<TResource> AddCompositeDateTimeSearchParam(
            string paramName,
            CompositeDateTimeExtractor<TResource> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                SearchParamType.Date,
                DateTimeSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddCompositeQuantitySearchParam<TCollection>(
            string paramName,
            CompositeQuantityExtractor<TResource, TCollection> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                SearchParamType.Quantity,
                QuantitySearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddCompositeReferenceSearchParam<TCollection>(
            string paramName,
            CompositeReferenceExtractor<TResource, TCollection> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                SearchParamType.Reference,
                ReferenceSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddCompositeStringSearchParam(
            string paramName,
            CompositeStringExtractor<TResource> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                SearchParamType.String,
                StringSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddCompositeTokenSearchParam<TCollection>(
            string paramName,
            CompositeTokenExtractor<TResource, TCollection> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                SearchParamType.Token,
                TokenSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddDateTimeSearchParam<TCollection>(
            string paramName,
            DateTimeExtractor<TResource, TCollection> extractor)
        {
            AddOrUpdateSearchParam(
                   paramName,
                   DateTimeSearchValue.Parse,
                   extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddNumberSearchParam(
            string paramName,
            NumberExtractor<TResource> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                NumberSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddQuantitySearchParam<TCollection>(
            string paramName,
            QuantityExtractor<TResource, TCollection> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                QuantitySearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddReferenceSearchParam<TCollection>(
            string paramName,
            ReferenceExtractor<TResource, TCollection> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                ReferenceSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddStringSearchParam(
             string paramName,
             StringExtractor<TResource> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                StringSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddTokenSearchParam<TCollection>(
            string paramName,
            TokenExtractor<TResource, TCollection> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                TokenSearchValue.Parse,
                extractor);

            return this;
        }

        internal ResourceTypeManifestBuilder<TResource> AddUriSearchParam(
            string paramName,
            UriExtractor<TResource> extractor)
        {
            AddOrUpdateSearchParam(
                paramName,
                UriSearchValue.Parse,
                extractor);

            return this;
        }

        private void AddOrUpdateSearchParam(
            string paramName,
            SearchParamValueParser parser,
            ISearchValuesExtractor extractor)
        {
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));
            EnsureArg.IsNotNull(extractor, nameof(extractor));

            if (!_searchParams.TryGetValue(paramName, out SearchParam searchParam))
            {
                searchParam = _searchParamFactory.CreateSearchParam(typeof(TResource), paramName, parser);

                _searchParams.Add(paramName, searchParam);
            }

            searchParam.AddExtractor(extractor);
        }

        private void AddOrUpdateSearchParam(
            string paramName,
            SearchParamType underlyingSearchParamType,
            SearchParamValueParser parser,
            ISearchValuesExtractor extractor)
        {
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));
            EnsureArg.IsNotNull(extractor, nameof(extractor));

            if (!_searchParams.TryGetValue(paramName, out SearchParam searchParam))
            {
                searchParam = _searchParamFactory.CreateCompositeSearchParam(
                    typeof(TResource),
                    paramName,
                    underlyingSearchParamType,
                    parser);

                _searchParams.Add(paramName, searchParam);
            }

            searchParam.AddExtractor(extractor);
        }

        private static IEnumerable<TCollection> ExtractCollection<TCollection>(
            Func<TResource, IEnumerable<TCollection>> collectionSelector,
            TResource resource)
        {
            return collectionSelector(resource)?
                .Where(item => item != null) ??
                Enumerable.Empty<TCollection>();
        }

        private static IEnumerable<Coding> ExtractCoding<TInput>(
            Func<TInput, CodeableConcept> compositeTokenSelector,
            TInput input)
        {
            return compositeTokenSelector(input)?.Coding?
                .Where(coding => coding != null && !coding.IsEmpty()) ??
                Enumerable.Empty<Coding>();
        }
    }
}
