// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Extension methods for parsing strings.
    /// </summary>
    internal static class StringExtensions
    {
        /// <summary>
        /// The character used to escape special characters.
        /// </summary>
        private const char EscapingCharacter = '\\';

        /// <summary>
        /// The character used to separate token system and code values.
        /// </summary>
        private const char TokenSeparator = '|';

        /// <summary>
        /// The character used to separate composite value.
        /// </summary>
        private const char CompositeSeparator = '$';

        /// <summary>
        /// The character used to separate values for or operation.
        /// </summary>
        private const char OrSeparator = ',';

        /// <summary>
        /// The escaped escaping character.
        /// </summary>
        private static readonly string EscapedEscapingCharacter = $"{EscapingCharacter}{EscapingCharacter}";

        /// <summary>
        /// The escaped token separator.
        /// </summary>
        private static readonly string EscapedTokenSeparator = $"{EscapingCharacter}{TokenSeparator}";

        /// <summary>
        /// The escaped composite separator.
        /// </summary>
        private static readonly string EscapedCompositeSeparator = $"{EscapingCharacter}{CompositeSeparator}";

        /// <summary>
        /// The escaped or separator.
        /// </summary>
        private static readonly string EscapedOrSeparator = $"{EscapingCharacter}{OrSeparator}";

        /// <summary>
        /// Splits the <paramref name="s"/> using token separator.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An <see cref="IList{T}"/> that contains strings split by using token separator.</returns>
        /// <remarks>Since the token separator character has special rule defined, if the character appears in the actual value,
        /// it needs to be escaped. More detail can be found here: http://hl7.org/fhir/search.html#escaping </remarks>
        public static IReadOnlyList<string> SplitByTokenSeparator(this string s)
        {
            EnsureArg.IsNotNull(s, nameof(s));

            return Split(s, TokenSeparator);
        }

        /// <summary>
        /// Splits the <paramref name="s"/> using composite separator.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An <see cref="IList{T}"/> that contains strings split by using composite separator.</returns>
        /// <remarks>Since the composite separator character has special rule defined, if the character appears in the actual value,
        /// it needs to be escaped. More detail can be found here: http://hl7.org/fhir/search.html#escaping </remarks>
        public static IReadOnlyList<string> SplitByCompositeSeparator(this string s)
        {
            EnsureArg.IsNotNull(s, nameof(s));

            return Split(s, CompositeSeparator);
        }

        /// <summary>
        /// Splits the <paramref name="s"/> using or separator.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An <see cref="IList{T}"/> that contains strings split by using or separator.</returns>
        /// <remarks>Since the or separator character has special rule defined, if the character appears in the actual value,
        /// it needs to be escaped. More detail can be found here: http://hl7.org/fhir/search.html#escaping </remarks>
        public static IReadOnlyList<string> SplitByOrSeparator(this string s)
        {
            EnsureArg.IsNotNull(s, nameof(s));

            return Split(s, OrSeparator);
        }

        /// <summary>
        /// Joins the <paramref name="strings"/>> using or separator.
        /// </summary>
        /// <param name="strings">String to be joined.</param>
        /// <returns>Single string of concatenated <paramref name="strings"/> by using or separator. </returns>
        public static string JoinByOrSeparator(this IEnumerable<string> strings)
        {
            EnsureArg.IsNotNull(strings, nameof(strings));
            return string.Join(OrSeparator, strings);
        }

        /// <summary>
        /// Escapes the search parameter value.
        /// </summary>
        /// <param name="s">The string to be escaped.</param>
        /// <returns>Escaped string.</returns>
        public static string EscapeSearchParameterValue(this string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            // Escaping character has to be escaped first since the rest of the escaping uses escaping character.
            s = s.Replace($"{EscapingCharacter}", EscapedEscapingCharacter, StringComparison.Ordinal);
            s = s.Replace($"{TokenSeparator}", EscapedTokenSeparator, StringComparison.Ordinal);
            s = s.Replace($"{CompositeSeparator}", EscapedCompositeSeparator, StringComparison.Ordinal);
            s = s.Replace($"{OrSeparator}", EscapedOrSeparator, StringComparison.Ordinal);

            return s;
        }

        /// <summary>
        /// Unescape the search parameter value.
        /// </summary>
        /// <param name="s">The string to be unescaped.</param>
        /// <returns>Unescaped string.</returns>
        public static string UnescapeSearchParameterValue(this string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s;
            }

            s = s.Replace(EscapedTokenSeparator, $"{TokenSeparator}", StringComparison.Ordinal);
            s = s.Replace(EscapedCompositeSeparator, $"{CompositeSeparator}", StringComparison.Ordinal);
            s = s.Replace(EscapedOrSeparator, $"{OrSeparator}", StringComparison.Ordinal);

            // Escaping character has to be escaped last.
            s = s.Replace(EscapedEscapingCharacter, $"{EscapingCharacter}", StringComparison.Ordinal);

            return s;
        }

        private static List<string> Split(string s, char separator)
        {
            EnsureArg.IsNotNull(s, nameof(s));

            var results = new List<string>();

            bool isEscaping = false;

            int currentSubstringStartingIndex = 0;

            for (int index = 0; index < s.Length; index++)
            {
                if (isEscaping)
                {
                    isEscaping = false;
                }
                else if (s[index] == EscapingCharacter)
                {
                    isEscaping = true;
                }
                else if (s[index] == separator)
                {
                    results.Add(s.Substring(currentSubstringStartingIndex, index - currentSubstringStartingIndex));
                    currentSubstringStartingIndex = index + 1;
                }
            }

            results.Add(s.Substring(currentSubstringStartingIndex, s.Length - currentSubstringStartingIndex));

            return results;
        }
    }
}
