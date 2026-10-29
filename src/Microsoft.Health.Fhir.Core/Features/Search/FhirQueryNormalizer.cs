// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    internal static class FhirQueryNormalizer
    {
        internal const int MaximumLength = 1024;

        internal static string Normalize(
            string resourceType,
            IEnumerable<string> parameterNames,
            string compartmentType = null,
            bool isHistory = false,
            IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)> sort = null,
            bool countOnly = false)
        {
            bool hasSort = sort?.Count > 0;
            List<string> normalizedParameterNames = parameterNames?
                .Where(name => !hasSort || name != KnownQueryParameterNames.Sort)
                .Select(Sanitize)
                .ToList() ?? [];

            if (hasSort)
            {
                string normalizedSort = string.Join(
                    ",",
                    sort.Select(item => $"{(item.sortOrder == SortOrder.Descending ? "desc" : "asc")}:{Sanitize(item.searchParameter.Code)}"));
                normalizedParameterNames.Add($"{KnownQueryParameterNames.Sort}={normalizedSort}");
            }

            if (countOnly)
            {
                normalizedParameterNames.Add("_querymode=count");
            }

            normalizedParameterNames.Sort(StringComparer.Ordinal);

            string normalizedResourceType = Sanitize(string.IsNullOrWhiteSpace(resourceType) ? KnownResourceTypes.Resource : resourceType);
            string searchScope = string.IsNullOrWhiteSpace(compartmentType)
                ? normalizedResourceType
                : $"{Sanitize(compartmentType)}/$compartment/{normalizedResourceType}";
            if (isHistory)
            {
                searchScope += "/_history";
            }

            string normalizedQuery = normalizedParameterNames.Count == 0
                ? searchScope
                : $"{searchScope}?{string.Join("&", normalizedParameterNames)}";

            if (normalizedQuery.Length <= MaximumLength)
            {
                return normalizedQuery;
            }

            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedQuery)));
            return $"{normalizedQuery[..(MaximumLength - hash.Length - 1)]}~{hash}";
        }

        private static string Sanitize(string input)
        {
            return string.Concat((input ?? string.Empty).Select(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or ':' or '$' ? character : '_'))
                .Replace("--", "__", StringComparison.Ordinal);
        }
    }
}
