// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Provides information about a search parameter.
    /// </summary>
    public class SearchParam : ISearchParam
    {
        private List<ISearchValuesExtractor> _searchValuesExtractors = new List<ISearchValuesExtractor>();

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParam"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <param name="paramType">The parameter type.</param>
        /// <param name="parser">The parser used to parse the string representation of the search parameter.</param>
        internal SearchParam(
            Type resourceType,
            string paramName,
            SearchParamType paramType,
            SearchParamValueParser parser)
        {
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsTrue(typeof(Resource).IsAssignableFrom(resourceType), nameof(resourceType));
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));
            Debug.Assert(
                Enum.IsDefined(typeof(SearchParamType), paramType),
                $"The value '{paramType}' is not a valid {nameof(SearchParamType)}.");
            EnsureArg.IsNotNull(parser, nameof(parser));

            ResourceType = resourceType;
            ParamName = paramName;
            ParamType = paramType;
            Parser = parser;
        }

        /// <inheritdoc />
        public Type ResourceType { get; }

        /// <inheritdoc />
        public string ParamName { get; }

        public SearchParamType ParamType { get; }

        protected SearchParamValueParser Parser { get; }

        /// <inheritdoc />
        public IEnumerable<ISearchValue> ExtractValues(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsOfType(resource.GetType(), ResourceType, nameof(resource));

            List<ISearchValue> results = new List<ISearchValue>();

            foreach (ISearchValuesExtractor extractor in _searchValuesExtractors)
            {
                IReadOnlyCollection<ISearchValue> searchValues = extractor.Extract(resource);

                if (searchValues != null)
                {
                    results.AddRange(searchValues.Where(sv => sv != null));
                }
            }

            return results;
        }

        /// <inheritdoc />
        public virtual ISearchValue Parse(string value)
        {
            EnsureArg.IsNotNullOrEmpty(value, nameof(value));

            return Parser(value);
        }

        void ISearchParam.AddExtractor(ISearchValuesExtractor extractor)
        {
            AddExtractor(extractor);
        }

        internal void AddExtractor(ISearchValuesExtractor extractor)
        {
            EnsureArg.IsNotNull(extractor, nameof(extractor));

            _searchValuesExtractors.Add(extractor);
        }
    }
}
