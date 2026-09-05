// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    internal static class FhirQueryNormalizer
    {
        internal const int MaximumLength = 256;

        internal static string Normalize(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            string compartmentType = null,
            bool isHistory = false)
        {
            string[] parameterNames = queryParameters?
                .Select(parameter => parameter.Item1)
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

            string normalizedQuery = parameterNames.Length == 0
                ? searchScope
                : $"{searchScope}?{string.Join("&", parameterNames)}";

            return normalizedQuery.Length <= MaximumLength
                ? normalizedQuery
                : $"{normalizedQuery[..(MaximumLength - 1)]}~";
        }

        private static string Sanitize(string input)
        {
            var result = new StringBuilder(input?.Length ?? 0);

            foreach (char character in input ?? string.Empty)
            {
                result.Append(IsSafeCharacter(character) ? character : '_');
            }

            return result.ToString().Replace("--", "__", StringComparison.Ordinal);
        }

        private static bool IsSafeCharacter(char character)
        {
            return (character >= 'A' && character <= 'Z')
                || (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character is '-' or '.' or '_' or ':' or '$';
        }
    }
}
