// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents a search index entry.
    /// </summary>
    public class SearchIndexEntry : IEquatable<SearchIndexEntry>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchIndexEntry"/> class.
        /// </summary>
        /// <remarks>
        /// searchParameter and value parameters should not have a null value
        /// But if the input json is not in the correct format then we are parsing the body here <see cref="FhirJsonInputFormatter"/> and passing the initial validations for required fields here <see cref="ModelAttributeValidator"/>
        /// e.g. If the body contains Coverage.status = "", then after parsing Coverage.status = null & Coverage.statusElement = null, resulting into minimum cardinality error as expected
        /// If the body contains Coverage.status = , then after parsing Coverage.status = null & Coverage.statusElement = {value=null}, which passes the Firely validation and CodeToTokenSearchValueConverter returns null
        /// In this case return BadRequestException with a valid message instead of 500
        /// </remarks>
        /// <param name="searchParameter">The search parameter</param>
        /// <param name="value">The searchable value.</param>
        public SearchIndexEntry(SearchParameterInfo searchParameter, ISearchValue value)
        {
            if (searchParameter is null)
            {
                throw new BadRequestException(string.Format(Resources.ValueCannotBeNull, "Search Parameter"));
            }

            if (value is null)
            {
                throw new BadRequestException(string.Format(Resources.ValueCannotBeNull, searchParameter.Expression));
            }

            SearchParameter = searchParameter;
            Value = value;
        }

        /// <summary>
        /// Gets the search parameter
        /// </summary>
        public SearchParameterInfo SearchParameter { get; }

        /// <summary>
        /// Gets the searchable value.
        /// </summary>
        public ISearchValue Value { get; }

        public static bool operator ==(SearchIndexEntry left, SearchIndexEntry right)
        {
            if (((object)left) == null && ((object)right) == null)
            {
                return true;
            }
            else if (((object)left) == null)
            {
                return false;
            }
            else
            {
                return left.Equals(right);
            }
        }

        public static bool operator !=(SearchIndexEntry left, SearchIndexEntry right)
        {
            return !(left == right);
        }

        public bool Equals(SearchIndexEntry other)
        {
            if (other == null)
            {
                return false;
            }

            if (other.SearchParameter.Url == SearchParameter.Url &&
                other.SearchParameter.Code.Equals(SearchParameter.Code, StringComparison.OrdinalIgnoreCase) &&
                other.Value == Value)
            {
                return true;
            }

            return false;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            SearchIndexEntry other = obj as SearchIndexEntry;
            if (other == null)
            {
                return false;
            }
            else
            {
                return Equals(other);
            }
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                SearchParameter.Code.GetHashCode(System.StringComparison.OrdinalIgnoreCase),
                SearchParameter.Url?.GetHashCode(),
                Value.GetHashCode());
        }
    }
}
