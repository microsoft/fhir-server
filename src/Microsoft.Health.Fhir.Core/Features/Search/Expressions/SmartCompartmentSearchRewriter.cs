// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Builds on the CompartmentSearchRewriter to add additional resources for Smart access
    /// </summary>
    public class SmartCompartmentSearchRewriter : ExpressionRewriterWithInitialContext<object>
    {
        private const string DevicePatientSearchParameterCode = "patient";

        private readonly Lazy<ISearchParameterDefinitionManager> _searchParameterDefinitionManager;
        private readonly CompartmentSearchRewriter _compartmentSearchRewriter;
        private readonly CoreFeatureConfiguration _coreFeatures;

        public SmartCompartmentSearchRewriter(
            CompartmentSearchRewriter compartmentSearchRewriter,
            Lazy<ISearchParameterDefinitionManager> searchParameterDefinitionManager,
            IOptions<CoreFeatureConfiguration> coreFeatures)
        {
            _compartmentSearchRewriter = EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
        }

        /// <summary>
        /// Gets resource types that are shared across SMART patient compartments.
        /// </summary>
        public static IReadOnlyCollection<string> UniversalResourceTypes { get; } = Array.AsReadOnly<string>(
        [
            KnownResourceTypes.Location,
            KnownResourceTypes.Organization,
            KnownResourceTypes.Practitioner,
            KnownResourceTypes.Medication,
            KnownCompartmentTypes.Device,
        ]);

        public override Expression VisitSmartCompartment(SmartCompartmentSearchExpression expression, object context)
        {
            SearchParameterInfo resourceTypeSearchParameter = _searchParameterDefinitionManager.Value.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);
            SearchParameterInfo idSearchParameter = _searchParameterDefinitionManager.Value.GetSearchParameter(expression.CompartmentType, SearchParameterNames.Id);

            var compartmentType = expression.CompartmentType;
            var compartmentId = expression.CompartmentId;

            // A smart user compartment is used to filter all search results by the resources available to the smart user.
            // The smart user has access to 3 things:
            // 1 - resources that are formal members of the FHIR compartment
            // 2 - their own resource
            // 3 - any "universal" resources, such as Locations and Medications

            // First a collection of any resources which refer to the smart user
            // we use the CompartmentSearchRewriter to get this list as it matches what we want
            // SmartCompartmentSearchExpression has filteredResourceTypes list which CompartmentSearchRewriter will use to only return relevant resource types from compartment search
            var expressionList = _compartmentSearchRewriter.BuildCompartmentSearchExpressionsGroup(expression).ToList();

            // Second the main resource
            // Earlier this was building SQL on the Resource table with just ResourceId clause
            // Do below to add ResourceTypeId clause. We will also be adding IsHistory and IsDeleted clause in union table handler
            var expressionForResourceItself = new List<Expression>();
            expressionForResourceItself.Add(Expression.SearchParameter(idSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, compartmentId, false)));
            expressionForResourceItself.Add(Expression.SearchParameter(resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, compartmentType, false)));
            expressionList.Add(Expression.And(expressionForResourceItself.ToArray()));

            // Some resource types have conditional visibility within the compartment (neither plainly universal nor
            // a plain formal member). GetConditionalCompartmentRules is the single source of truth for those rules;
            // it is consumed here to build the union legs and by SmartCompartmentMembershipContextFactory to build
            // the SQL include/revinclude candidate predicate, so the two paths cannot drift.
            IReadOnlyList<SmartCompartmentConditionalRule> conditionalRules = GetConditionalCompartmentRules(compartmentType);
            var conditionallyVisibleTypes = conditionalRules
                .Select(rule => rule.ResourceType)
                .ToHashSet(StringComparer.Ordinal);

            // Finally we add in the "universal" resources, which are resources that are not compartment specific.
            // Any type governed by a conditional rule is excluded here and contributed by the conditional legs below
            // instead. UniversalResourceTypes remains the single source of truth for the base universal set (it still
            // includes Device for consumers such as SmartCompartmentMembershipContextFactory).
            var universalResourceTypes = UniversalResourceTypes
                .Where(resourceType => !conditionallyVisibleTypes.Contains(resourceType))
                .ToList();

            // In case FilteredResourceTypes is specified and not the default, we need to filter down the universalResourceTypes to only those specified
            bool hasResourceTypeFilter = expression.FilteredResourceTypes.Any(resourceType => !string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.Ordinal));
            if (hasResourceTypeFilter)
            {
                universalResourceTypes = universalResourceTypes.Where(x => expression.FilteredResourceTypes.Contains(x)).ToList();
            }

            // if there are any universal resource types to add, add them in
            if (universalResourceTypes.Any())
            {
                if (universalResourceTypes.Count == 1)
                {
                    expressionList.Add(Expression.SearchParameter(resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, universalResourceTypes[0], false)));
                }
                else
                {
                    expressionList.Add(Expression.SearchParameter(resourceTypeSearchParameter, Expression.In(FieldName.TokenCode, null, universalResourceTypes)));
                }
            }

            foreach (SmartCompartmentConditionalRule rule in conditionalRules)
            {
                if (hasResourceTypeFilter && !expression.FilteredResourceTypes.Contains(rule.ResourceType, StringComparer.Ordinal))
                {
                    continue;
                }

                switch (rule.Visibility)
                {
                    case SmartCompartmentConditionalVisibility.HasNoReference:
                        // Resources with no value for the reference parameter are visible in every smart compartment.
                        // NotReferencingExpression is only supported by the SQL query generator (Cosmos DB support is retired).
                        expressionList.Add(Expression.NotReferencing(rule.ResourceType, rule.ReferenceSearchParameter));
                        break;

                    case SmartCompartmentConditionalVisibility.ReferencesCompartmentRoot:
                        // Resources referencing the compartment root. Same shape as the compartment leg built by
                        // SqlCompartmentSearchRewriter: an indexed seek on ReferenceSearchParam.
                        var typeRestriction = Expression.SearchParameter(resourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, rule.ResourceType, false));
                        expressionList.Add(Expression.And(
                            Expression.SearchParameter(rule.ReferenceSearchParameter, typeRestriction),
                            Expression.StringEquals(FieldName.ReferenceResourceType, null, compartmentType, false),
                            Expression.StringEquals(FieldName.ReferenceResourceId, null, compartmentId, false)));
                        break;
                }
            }

            // union all those results together
            return Expression.Union(UnionOperator.All, expressionList);
        }

        /// <summary>
        /// Determines whether the SMART Device compartment restriction applies for the current configuration.
        /// When it applies, Device is not treated as a universally shared resource; instead only devices that
        /// reference the compartment root (via Device.patient) or that have no patient reference at all are
        /// visible. The restriction relies on SQL-only expression support, so it is gated on the SQL compartment
        /// rewriter (Cosmos DB support is retired).
        /// </summary>
        /// <param name="devicePatientSearchParameter">The resolved Device.patient reference search parameter when the restriction applies; otherwise null.</param>
        /// <returns><c>true</c> when the Device restriction applies; otherwise <c>false</c>.</returns>
        public bool ShouldRestrictDevices(out SearchParameterInfo devicePatientSearchParameter)
        {
            devicePatientSearchParameter = null;
            return _coreFeatures.EnableSmartCompartmentDeviceRestriction &&
                _compartmentSearchRewriter is SqlCompartmentSearchRewriter &&
                _searchParameterDefinitionManager.Value.TryGetSearchParameter(KnownResourceTypes.Device, DevicePatientSearchParameterCode, out devicePatientSearchParameter);
        }

        /// <summary>
        /// Builds the declarative conditional-visibility rules for the given compartment. This is the single source
        /// of truth for resource types whose compartment visibility is conditional; it is consumed both by the
        /// compartment union (<see cref="VisitSmartCompartment"/>) and by the SQL include/revinclude candidate
        /// authorization predicate (via SmartCompartmentMembershipContextFactory), so the two paths cannot drift.
        /// </summary>
        /// <param name="compartmentType">The compartment root resource type (for example, Patient or Practitioner).</param>
        /// <returns>The conditional rules that apply for the compartment; empty when none apply.</returns>
        public IReadOnlyList<SmartCompartmentConditionalRule> GetConditionalCompartmentRules(string compartmentType)
        {
            if (!ShouldRestrictDevices(out SearchParameterInfo devicePatientSearchParameter))
            {
                return Array.Empty<SmartCompartmentConditionalRule>();
            }

            var rules = new List<SmartCompartmentConditionalRule>
            {
                // Devices with no Device.patient reference are visible in every smart compartment.
                new SmartCompartmentConditionalRule(KnownResourceTypes.Device, devicePatientSearchParameter, SmartCompartmentConditionalVisibility.HasNoReference),
            };

            // Devices assigned to the patient (Device.patient references the compartment root) are visible only in
            // the Patient compartment; Device.patient can never reference a non-Patient root.
            if (string.Equals(compartmentType, KnownResourceTypes.Patient, StringComparison.Ordinal))
            {
                rules.Add(new SmartCompartmentConditionalRule(KnownResourceTypes.Device, devicePatientSearchParameter, SmartCompartmentConditionalVisibility.ReferencesCompartmentRoot));
            }

            return rules;
        }
    }
}
