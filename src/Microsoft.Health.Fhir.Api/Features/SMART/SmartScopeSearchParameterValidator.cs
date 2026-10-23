// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Globalization;
using System.Linq;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Features.Smart
{
    /// <summary>
    /// Validates search parameter values in SMART on FHIR clinical scopes.
    /// </summary>
    internal static class SmartScopeSearchParameterValidator
    {
        // Maximum number of fractional (or integer) decimal digits supported for numeric values in SMART clinical
        // scope search parameter constraints.
        // Note: SQL Server indexes NumberSearchParam and QuantitySearchParam values in decimal(36, 18) columns
        // (18 integer digits and 18 fractional digits). When FHIR search builds the implicit range for a number
        // or quantity (SearchValueExpressionBuilderHelper.GenerateNumberExpression), it adds and subtracts half a
        // unit at the next decimal place (±0.5 * 10^-scale), which requires (scale + 1) fractional digits, and
        // for 'ap' also ±10% of the value. A scope value with 18 fractional digits therefore produces
        // 19-fractional-digit bounds that overflow the scale of decimal(36, 18) and are silently rounded by SQL
        // Server, altering matching semantics and widening the granted scope (and 19+ fractional digits round
        // both the value and its bounds). Limiting decimal precision in SMART scopes to 17 digits (matching the
        // FHIR R5 decimal limit and W3C XML Schema's 18-digit minimum) guarantees that both the value and its
        // expanded range bounds are represented without rounding or overflow.
        internal const int MaxSupportedDecimalDigits = 17;

        // FHIR search comparator prefixes (https://hl7.org/fhir/R4/search.html#prefix).
        private static readonly string[] SearchComparatorPrefixes = Enum.GetValues<SearchComparator>()
            .Select(c => c.GetLiteral())
            .ToArray();

        /// <summary>
        /// Returns true when any numeric value in the search parameter value (including comma-separated OR lists,
        /// '$' composite components, and 'number|system|code' quantity tuples with optional comparator prefixes)
        /// exceeds <see cref="MaxSupportedDecimalDigits"/> (17 digits), which would cause silent rounding or
        /// overflow when expanding implicit search range bounds (±0.5 * 10^-scale) against SQL decimal(36, 18).
        /// </summary>
        /// <param name="paramValue">The search parameter value to inspect.</param>
        /// <returns>True if any numeric value in <paramref name="paramValue"/> exceeds supported precision; otherwise, false.</returns>
        internal static bool ExceedsSupportedDecimalPrecision(string paramValue)
        {
            if (string.IsNullOrEmpty(paramValue))
            {
                return false;
            }

            foreach (string orPart in paramValue.SplitByOrSeparator())
            {
                foreach (string componentPart in orPart.SplitByCompositeSeparator())
                {
                    IReadOnlyList<string> tokenParts = componentPart.SplitByTokenSeparator();
                    if (tokenParts.Count == 0)
                    {
                        continue;
                    }

                    ReadOnlySpan<char> candidate = StripComparatorPrefix(tokenParts[0].AsSpan());
                    if (IsDecimalValueWithExcessivePrecision(candidate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static ReadOnlySpan<char> StripComparatorPrefix(ReadOnlySpan<char> value)
        {
            foreach (string prefix in SearchComparatorPrefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return value.Slice(prefix.Length);
                }
            }

            return value;
        }

        private static bool IsDecimalValueWithExcessivePrecision(ReadOnlySpan<char> candidate)
        {
            if (candidate.IsEmpty)
            {
                return false;
            }

            // System.Decimal supports 28-29 digits and preserves trailing zeros (including negative exponents)
            // in Scale up to 28, which exceeds MaxSupportedDecimalDigits (17). SqlDecimal computes both total
            // Precision and fractional Scale directly from the parsed decimal.
            if (decimal.TryParse(candidate, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal parsedValue))
            {
                var sqlDecimal = new SqlDecimal(parsedValue);
                return sqlDecimal.Scale > MaxSupportedDecimalDigits
                    || sqlDecimal.Precision - sqlDecimal.Scale > MaxSupportedDecimalDigits
                    || sqlDecimal.Precision > MaxSupportedDecimalDigits + 1;
            }

            // If the token is a valid floating-point number that overflowed System.Decimal (> 28-29 digits),
            // it also exceeds the supported decimal precision.
            return double.TryParse(candidate, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double d)
                && !double.IsNaN(d);
        }
    }
}
