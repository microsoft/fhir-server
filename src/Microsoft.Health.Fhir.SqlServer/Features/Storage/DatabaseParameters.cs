// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// The numeric settings in one partition's <c>dbo.Parameters</c> table (feature flags and thresholds ops can change
    /// without redeploying).
    /// </summary>
    /// <remarks>
    /// IDs match case-insensitively and ignore trailing spaces, like the SQL lookup this replaces under the default
    /// database collation.
    /// </remarks>
    public sealed class DatabaseParameters
    {
        private readonly FrozenDictionary<string, double> _numbers;

        internal DatabaseParameters(IReadOnlyDictionary<string, double> numbers)
        {
            EnsureArg.IsNotNull(numbers, nameof(numbers));

            var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach ((string id, double number) in numbers)
            {
                normalized[id.TrimEnd()] = number;
            }

            _numbers = normalized.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the settings to use when <c>dbo.Parameters</c> can't be read: every parameter takes its default.
        /// </summary>
        internal static DatabaseParameters Empty { get; } = new(new Dictionary<string, double>());

        /// <summary>
        /// Gets a numeric parameter.
        /// </summary>
        /// <param name="parameterId">The parameter ID.</param>
        /// <param name="defaultValue">The value when the parameter has no number.</param>
        /// <returns>The parameter's number, or <paramref name="defaultValue"/>.</returns>
        public double GetNumber(string parameterId, double defaultValue) =>
            _numbers.TryGetValue(EnsureArg.IsNotNull(parameterId, nameof(parameterId)).TrimEnd(), out double value) ? value : defaultValue;

        /// <summary>
        /// Gets a feature flag, where 1 means enabled and any other number means disabled.
        /// </summary>
        /// <param name="parameterId">The parameter ID.</param>
        /// <param name="defaultValue">Whether the flag is enabled when the parameter has no number.</param>
        /// <returns>Whether the flag is enabled.</returns>
        public bool IsEnabled(string parameterId, bool defaultValue) =>
            Convert.ToInt32(GetNumber(parameterId, defaultValue ? 1 : 0)) == 1;
    }
}
