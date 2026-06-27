// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public static class FhirRequestContextExtensions
    {
        /// <summary>
        /// The <see cref="IFhirRequestContext.Properties"/> key under which per-request server
        /// configuration overrides are stashed. The value is an
        /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> keyed by the bare configuration name
        /// (case-insensitive) with the raw string value.
        /// </summary>
        internal const string RequestConfigurationOverridesPropertyName = "Request.ConfigurationOverrides";

        public static DateTimeOffset? GetSearchParameterLastUpdated(this IFhirRequestContext context)
        {
            if (context?.Properties.TryGetValue(SearchParameterRequestContextPropertyNames.LastUpdated, out var value) == true)
            {
                return (DateTimeOffset)value;
            }

            return null;
        }

        public static void SetSearchParameterLastUpdated(this IFhirRequestContext context, DateTimeOffset? lastUpdated)
        {
            if (lastUpdated.HasValue && context != null)
            {
                context.Properties[SearchParameterRequestContextPropertyNames.LastUpdated] = lastUpdated.Value;
            }
        }

        public static void ClearSearchParameterLastUpdated(this IFhirRequestContext context)
        {
            context?.Properties.Remove(SearchParameterRequestContextPropertyNames.LastUpdated);
        }

        /// <summary>
        /// Stores the supplied per-request configuration overrides on the request context. Intended to be
        /// populated only by the test-only request-configuration-override middleware. A null/empty set is
        /// ignored so the property is never created when there is nothing to override.
        /// </summary>
        /// <param name="context">The request context. May be null.</param>
        /// <param name="overrides">The override values keyed by bare configuration name.</param>
        public static void SetRequestConfigurationOverrides(this IFhirRequestContext context, IReadOnlyDictionary<string, string> overrides)
        {
            if (context != null && overrides != null && overrides.Count > 0)
            {
                context.Properties[RequestConfigurationOverridesPropertyName] = overrides;
            }
        }

        /// <summary>
        /// Attempts to read a per-request configuration override value by its bare configuration name.
        /// </summary>
        /// <param name="context">The request context. May be null.</param>
        /// <param name="key">The bare configuration name (case-insensitive).</param>
        /// <param name="value">The raw override value when present.</param>
        /// <returns><c>true</c> if an override was found; otherwise <c>false</c>.</returns>
        public static bool TryGetRequestConfigurationOverride(this IFhirRequestContext context, string key, out string value)
        {
            value = null;

            if (context?.Properties != null &&
                context.Properties.TryGetValue(RequestConfigurationOverridesPropertyName, out var raw) &&
                raw is IReadOnlyDictionary<string, string> overrides &&
                overrides.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a boolean per-request configuration override by its bare configuration name, returning
        /// <c>null</c> when no (parseable) override is present so callers can fall back to the server default.
        /// </summary>
        /// <param name="context">The request context. May be null.</param>
        /// <param name="key">The bare configuration name (case-insensitive).</param>
        /// <returns>The overridden boolean value, or <c>null</c> when not overridden.</returns>
        public static bool? GetBooleanConfigurationOverride(this IFhirRequestContext context, string key)
        {
            if (context.TryGetRequestConfigurationOverride(key, out var raw) && bool.TryParse(raw, out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
