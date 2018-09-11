// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Provides support for creating <see cref="SearchParam"/> objects.
    /// </summary>
    public class SearchParamFactory : ISearchParamFactory
    {
        private readonly ISearchParamDefinitionManager _searchParamManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParamFactory"/> class.
        /// </summary>
        /// <param name="searchParamManager">The search parameter manager.</param>
        public SearchParamFactory(ISearchParamDefinitionManager searchParamManager)
        {
            EnsureArg.IsNotNull(searchParamManager, nameof(searchParamManager));

            _searchParamManager = searchParamManager;
        }

        /// <inheritdoc />
        public SearchParam CreateSearchParam(
            Type resourceType,
            string paramName,
            SearchParamValueParser parser)
        {
            ValidateParameters(resourceType, paramName, parser);

            SearchParamType searchParamType = _searchParamManager.GetSearchParamType(resourceType, paramName);

            if (searchParamType == SearchParamType.Reference)
            {
                IReadOnlyCollection<Type> targetTypes = _searchParamManager.GetReferenceTargetResourceTypes(resourceType, paramName);

                return new ReferenceSearchParam(resourceType, paramName, parser, targetTypes);
            }

            return new SearchParam(resourceType, paramName, searchParamType, parser);
        }

        /// <inheritdoc />
        public SearchParam CreateCompositeSearchParam(
            Type resourceType,
            string paramName,
            SearchParamType underlyingSearchParamType,
            SearchParamValueParser parser)
        {
            ValidateParameters(resourceType, paramName, parser);

            return new CompositeSearchParam(resourceType, paramName, underlyingSearchParamType, parser);
        }

        private static void ValidateParameters(
            Type resourceType,
            string paramName,
            SearchParamValueParser parser)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsTrue(typeof(Resource).IsAssignableFrom(resourceType), nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));
            EnsureArg.IsNotNull(parser, nameof(parser));
        }
    }
}
