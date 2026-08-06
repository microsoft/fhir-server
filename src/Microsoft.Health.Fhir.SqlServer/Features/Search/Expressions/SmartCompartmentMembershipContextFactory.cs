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
        public static SmartCompartmentMembershipContext Create(
            Expression expression,
            SqlCompartmentSearchRewriter compartmentSearchRewriter,
            SmartCompartmentSearchRewriter smartCompartmentSearchRewriter = null)
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

            // Conditional-visibility rules (for example the SMART Device limits) come from the same single source
            // of truth the compartment union uses (SmartCompartmentSearchRewriter.GetConditionalCompartmentRules),
            // so the union path and this candidate predicate cannot drift. A type governed by a conditional rule is
            // NOT universally shared: it is authorized only by its conditional leg in the SQL generator (own device
            // referencing the compartment root, or unassigned device with no patient reference).
            IReadOnlyList<SmartCompartmentConditionalRule> conditionalRules =
                smartCompartmentSearchRewriter?.GetConditionalCompartmentRules(smartCompartment.CompartmentType)
                ?? Array.Empty<SmartCompartmentConditionalRule>();

            ImmutableArray<SmartCompartmentConditionalMembershipRule> conditionalMembershipRules = conditionalRules
                .Select(rule => new SmartCompartmentConditionalMembershipRule(
                    rule.ResourceType,
                    rule.ReferenceSearchParameter.Url.AbsoluteUri,
                    rule.Visibility))
                .ToImmutableArray();

            // Same single source of truth as the compartment union: universal types minus conditionally
            // visible types (see SmartCompartmentSearchRewriter.GetSharedResourceTypes).
            ImmutableArray<string> sharedResourceTypes =
                SmartCompartmentSearchRewriter.GetSharedResourceTypes(conditionalRules).ToImmutableArray();

            return new SmartCompartmentMembershipContext(
                smartCompartment.CompartmentType,
                smartCompartment.CompartmentId,
                sharedResourceTypes,
                rules,
                conditionalMembershipRules);
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
