// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

/// <summary>
/// Registry of allowlisted search parameters that have single-point search behavior.
/// </summary>
internal static class SinglePointSearchParameterRegistry
{
    private static readonly Dictionary<string, SinglePointSearchBehavior> AllowlistedParameters = new(StringComparer.Ordinal)
    {
        { "http://hl7.org/fhir/SearchParameter/individual-birthdate", SinglePointSearchBehavior.SinglePointDateTime },
    };

    /// <summary>
    /// Gets the single-point search behavior for the specified search parameter.
    /// </summary>
    /// <param name="searchParameterUrl">The URL of the search parameter.</param>
    /// <returns>The single-point search behavior, or <see cref="SinglePointSearchBehavior.None"/> if not allowlisted.</returns>
    public static SinglePointSearchBehavior GetBehavior(string searchParameterUrl)
    {
        if (string.IsNullOrEmpty(searchParameterUrl))
        {
            return SinglePointSearchBehavior.None;
        }

        return AllowlistedParameters.TryGetValue(searchParameterUrl, out var behavior) ? behavior : SinglePointSearchBehavior.None;
    }

    /// <summary>
    /// Gets the single-point search behavior for the specified search parameter info.
    /// </summary>
    /// <param name="searchParameterInfo">The search parameter info.</param>
    /// <returns>The single-point search behavior, or <see cref="SinglePointSearchBehavior.None"/> if not allowlisted.</returns>
    public static SinglePointSearchBehavior GetBehavior(SearchParameterInfo searchParameterInfo)
    {
        if (searchParameterInfo?.Url == null)
        {
            return SinglePointSearchBehavior.None;
        }

        return GetBehavior(searchParameterInfo.Url.OriginalString);
    }
}
