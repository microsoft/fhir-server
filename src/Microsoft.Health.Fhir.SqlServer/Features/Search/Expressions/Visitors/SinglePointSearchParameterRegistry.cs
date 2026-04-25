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
internal class SinglePointSearchParameterRegistry
{
    private static readonly Dictionary<string, SinglePointSearchBehavior> AllowlistedParameters = new(StringComparer.Ordinal)
    {
        { "http://hl7.org/fhir/SearchParameter/individual-birthdate", SinglePointSearchBehavior.SinglePointDateTime },
    };

    /// <summary>
    /// Attempts to get the single-point search behavior for the specified search parameter.
    /// </summary>
    /// <param name="searchParameterInfo">The search parameter info.</param>
    /// <param name="behavior">The single-point search behavior, if found.</param>
    /// <returns>True if the parameter is allowlisted; false otherwise.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:MarkMembersAsStatic", Justification = "Instance method for extensibility in Task 3")]
    public bool TryGetBehavior(SearchParameterInfo searchParameterInfo, out SinglePointSearchBehavior behavior)
    {
        behavior = SinglePointSearchBehavior.None;

        if (searchParameterInfo?.Url == null)
        {
            return false;
        }

        var url = searchParameterInfo.Url.OriginalString;
        if (AllowlistedParameters.TryGetValue(url, out var foundBehavior))
        {
            behavior = foundBehavior;
            return true;
        }

        return false;
    }
}
