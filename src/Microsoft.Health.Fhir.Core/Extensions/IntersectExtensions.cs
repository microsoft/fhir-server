// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    internal static class IntersectExtensions
    {
        public static bool? IntersectBool(this bool? supported, bool? configured, IList<string> issues, string fieldName)
        {
            if (!supported.HasValue && !configured.HasValue)
            {
                return null;
            }

            if (supported.GetValueOrDefault() == false && configured.GetValueOrDefault())
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidBooleanConfigSetting, fieldName, configured.GetValueOrDefault()));
            }

            return supported.GetValueOrDefault() && configured.GetValueOrDefault();
        }

        /// <summary>
        /// Intersects two enums
        /// </summary>
        /// <typeparam name="T">Type of enum</typeparam>
        /// <param name="supported">The list of supported capabilities.</param>
        /// <param name="configured">The configured capability.</param>
        /// <param name="issues">List of issues found so far</param>
        /// <param name="fieldName">Configured field name</param>
        /// <returns>Valid supported enum</returns>
        public static T? IntersectEnum<T>(this IEnumerable<T> supported, T? configured, IList<string> issues, string fieldName)
            where T : struct, IConvertible
        {
            Debug.Assert(typeof(T).IsEnum, "Generic should be an enum type.");

            if (!configured.HasValue)
            {
                return null;
            }

            if (!supported.Contains(configured.Value))
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidEnumConfigSetting, fieldName, configured.Value, string.Join(",", supported.Select(s => s.ToString(CultureInfo.InvariantCulture)))));
                return supported.LastOrDefault();
            }

            return configured.Value;
        }

        public static List<TElement> IntersectList<TElement, TProperty>(this IEnumerable<TElement> supported, IEnumerable<TElement> configured, Func<TElement, TProperty> selector, IList<string> issues, string fieldName)
        {
            EnsureArg.IsNotNull(supported, nameof(supported));
            EnsureArg.IsNotNull(configured, nameof(configured));
            EnsureArg.IsNotNull(selector, nameof(selector));

            var shouldContain = supported.Select(selector).ToList();
            var config = configured.Where(x => shouldContain.Contains(selector(x))).OrderBy(x => selector(x)?.ToString());

            if (config.Count() != configured.Count())
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidListConfigSetting, fieldName));
            }

            if (config.Select(selector).GroupBy(x => x).Any(x => x.Count() > 1))
            {
                issues.Add(string.Format(CultureInfo.InvariantCulture, Core.Resources.InvalidListConfigDuplicateItem, fieldName));
            }

            return config.ToList();
        }
    }
}
