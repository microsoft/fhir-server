// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Provides information about a composite search parameter.
    /// </summary>
    internal class CompositeSearchParam : SearchParam
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeSearchParam"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <param name="underlyingSearchParamType">The underlying search parameter type of the composite search parameter.</param>
        /// <param name="parser">The parser used to parse the string representation of the search parameter.</param>
        internal CompositeSearchParam(
            Type resourceType,
            string paramName,
            SearchParamType underlyingSearchParamType,
            SearchParamValueParser parser)
            : base(resourceType, paramName, SearchParamType.Composite, parser)
        {
            Debug.Assert(
                Enum.IsDefined(typeof(SearchParamType), underlyingSearchParamType),
                $"The value '{underlyingSearchParamType}' is not a valid {nameof(SearchParamType)}.");

            UnderlyingSearchParamType = underlyingSearchParamType;
        }

        /// <summary>
        /// Gets the underlying search parameter type.
        /// </summary>
        internal SearchParamType UnderlyingSearchParamType { get; }

        /// <inheritdoc />
        public override ISearchValue Parse(string value)
        {
            return LegacyCompositeSearchValue.Parse(value, Parser);
        }
    }
}
