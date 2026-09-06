// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    internal static class FhirQueryNormalizer
    {
        internal const int MaximumLength = 256;

        internal static string Normalize(
            string resourceType,
            IEnumerable<string> parameterNames,
            string compartmentType = null,
            bool isHistory = false)
        {
            string[] normalizedParameterNames = parameterNames?
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(Sanitize)
                .ToArray() ?? [];

            string normalizedResourceType = Sanitize(string.IsNullOrWhiteSpace(resourceType) ? KnownResourceTypes.Resource : resourceType);
            string searchScope = string.IsNullOrWhiteSpace(compartmentType)
                ? normalizedResourceType
                : $"{Sanitize(compartmentType)}/$compartment/{normalizedResourceType}";
            if (isHistory)
            {
                searchScope += "/_history";
            }

            string normalizedQuery = normalizedParameterNames.Length == 0
                ? searchScope
                : $"{searchScope}?{string.Join("&", normalizedParameterNames)}";

            return normalizedQuery.Length <= MaximumLength
                ? normalizedQuery
                : $"{normalizedQuery[..(MaximumLength - 1)]}~";
        }

        private static string Sanitize(string input)
        {
            return string.Concat((input ?? string.Empty).Select(character =>
                    char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or ':' or '$' ? character : '_'))
                .Replace("--", "__", StringComparison.Ordinal);
        }
    }
}
