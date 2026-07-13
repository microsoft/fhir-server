// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// Creates SQL SMART compartment membership descriptions from Core search expressions.
    /// </summary>
    internal static class SmartCompartmentMembershipContextFactory
    {
        private static readonly ImmutableArray<string> SharedResourceTypes =
            SmartCompartmentSearchRewriter.UniversalResourceTypes.ToImmutableArray();

        public static SmartCompartmentMembershipContext Create(
            Expression expression,
            SqlCompartmentSearchRewriter compartmentSearchRewriter)
        {
            SmartCompartmentSearchExpression smartCompartment = FindSmartCompartment(expression);
            if (smartCompartment == null)
            {
                return null;
            }

            IReadOnlyDictionary<string, IReadOnlyCollection<SearchParameterInfo>> parametersByResourceType =
                compartmentSearchRewriter.GetMaterializedCompartmentSearchParameters(
                    smartCompartment.CompartmentType,
                    filteredResourceTypes: null,
                    includeMaterializedEquivalents: true);

            ImmutableArray<SmartCompartmentMembershipRule> rules = parametersByResourceType
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new SmartCompartmentMembershipRule(
                    pair.Key,
                    pair.Value
                        .Select(parameter => parameter.Url)
                        .Distinct()
                        .OrderBy(url => url.AbsoluteUri, StringComparer.Ordinal)
                        .ToImmutableArray()))
                .Where(rule => !rule.SearchParameterUrls.IsDefaultOrEmpty)
                .ToImmutableArray();

            return new SmartCompartmentMembershipContext(
                smartCompartment.CompartmentType,
                smartCompartment.CompartmentId,
                SharedResourceTypes,
                rules);
        }

        private static SmartCompartmentSearchExpression FindSmartCompartment(Expression expression)
        {
            if (expression is SmartCompartmentSearchExpression smartCompartment)
            {
                return smartCompartment;
            }

            if (expression is IExpressionsContainer container)
            {
                foreach (Expression child in container.Expressions)
                {
                    SmartCompartmentSearchExpression result = FindSmartCompartment(child);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }
    }
}
