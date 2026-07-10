// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Rewrites SqlCompartmentSearchRewriter to use main search index.
    /// </summary>
    public class SqlCompartmentSearchRewriter : CompartmentSearchRewriter
    {
        public SqlCompartmentSearchRewriter(
            Lazy<ICompartmentDefinitionManager> compartmentDefinitionManager,
            Lazy<ISearchParameterDefinitionManager> searchParameterDefinitionManager)
            : base(compartmentDefinitionManager, searchParameterDefinitionManager)
        {
        }

        public override List<Expression> BuildCompartmentSearchExpressionsGroup(CompartmentSearchExpression expression)
        {
            SearchParameterInfo resourceTypeSearchParameter = SearchParameterDefinitionManager.Value.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);

            var compartmentType = expression.CompartmentType;
            var compartmentId = expression.CompartmentId;

            if (Enum.TryParse(compartmentType, out ValueSets.CompartmentType parsedCompartmentType))
            {
                if (string.IsNullOrWhiteSpace(compartmentId))
                {
                    throw new InvalidSearchOperationException(Core.Resources.CompartmentIdIsInvalid);
                }

                var compartmentResourceTypesToSearch = new HashSet<string>();
                var searchParameterInfoList = new Dictionary<string, (SearchParameterInfo searchParameterInfo, HashSet<string> ResourceTypes)>();

                void AddCompartmentSearchParameter(SearchParameterInfo searchParameter, string resourceType)
                {
                    // Use the URL string as the key.
                    string searchParamUrl = searchParameter.Url.ToString();

                    if (searchParameterInfoList.TryGetValue(searchParamUrl, out var existing))
                    {
                        // Add the compartment resource type if the key exists.
                        existing.ResourceTypes.Add(resourceType);
                    }
                    else
                    {
                        // Otherwise, add a new dictionary entry.
                        searchParameterInfoList[searchParamUrl] = (searchParameter, new HashSet<string> { resourceType });
                    }
                }

                // A SMART compartment is broader than a regular compartment search: the smart user has access to any
                // resource that references them (see SmartCompartmentSearchRewriter). The FHIR compartment definition
                // maps several resource types (e.g. Encounter, Condition, Procedure, CareTeam) to the common `patient`
                // search parameter (clinical-patient). That parameter's FHIRPath uses resolve() and is therefore never
                // materialized as a ReferenceSearchParam index row, so a membership predicate keyed on it matches
                // nothing and would silently drop legitimately in-compartment resources.
                bool isSmartCompartment = expression is SmartCompartmentSearchExpression;

                // For a SMART compartment that spans every resource type (an unrestricted reversed wildcard such as
                // _revinclude=*:*, surfaced by SearchOptionsFactory as the DomainResource sentinel), enumerating a
                // per-(resource type, reference parameter) membership predicate for all compartment types produces
                // thousands of OR clauses. That union is re-generated wherever the compartment is re-applied (notably the
                // _include / _revinclude re-generation path), so the enumeration is emitted twice and dominates the query
                // text. Every enumerated member is ultimately constrained to ReferenceResourceId = compartmentId, so the
                // union is exactly "any resource that references the compartment root". For the all-types case we emit that
                // single predicate directly instead of enumerating it: it is equivalent (a superset only over reference
                // parameters that were previously unmapped, all still strictly bound to the compartment root, consistent
                // with the SMART "any resource which refers to them" model), still cannot disclose another compartment's
                // data, and is dramatically smaller.
                bool isAllTypesSmartCompartment = isSmartCompartment
                    && !expression.FilteredResourceTypes.Any(resourceType => !string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.Ordinal));

                if (isAllTypesSmartCompartment)
                {
                    return new List<Expression>
                    {
                        Expression.And(
                            Expression.StringEquals(FieldName.ReferenceResourceType, null, compartmentType, false),
                            Expression.StringEquals(FieldName.ReferenceResourceId, null, compartmentId, false)),
                    };
                }

                if (CompartmentDefinitionManager.Value.TryGetResourceTypes(parsedCompartmentType, out HashSet<string> resourceTypes))
                {
                    if (expression.FilteredResourceTypes.Any(resourceType => !string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.Ordinal)))
                    {
                        resourceTypes = resourceTypes.Where(x => expression.FilteredResourceTypes.Contains(x)).ToHashSet();
                    }

                    foreach (var resourceFilter in resourceTypes)
                    {
                        compartmentResourceTypesToSearch.Add(resourceFilter);
                    }
                }

                foreach (var compartmentResourceType in compartmentResourceTypesToSearch)
                {
                    if (CompartmentDefinitionManager.Value.TryGetSearchParams(compartmentResourceType, parsedCompartmentType, out HashSet<string> compartmentSearchParameters))
                    {
                        foreach (var compartmentSearchParameter in compartmentSearchParameters)
                        {
                            if (SearchParameterDefinitionManager.Value.TryGetSearchParameter(compartmentResourceType, compartmentSearchParameter, out SearchParameterInfo sp))
                            {
                                AddCompartmentSearchParameter(sp, compartmentResourceType);
                            }
                        }
                    }

                    // For SMART compartments, also include every materialized reference parameter of the resource type
                    // that can target the compartment root type. This keeps membership consistent with what is actually
                    // indexed (e.g. an Encounter is indexed under Encounter-subject, not the unmaterialized
                    // clinical-patient) and with the SMART model. It is additive and still constrained (below) to the
                    // compartment root id, so it can only admit resources that reference the compartment root itself and
                    // never widens the set beyond the caller's compartment.
                    if (isSmartCompartment)
                    {
                        foreach (SearchParameterInfo referenceParameter in SearchParameterDefinitionManager.Value.GetSearchParameters(compartmentResourceType))
                        {
                            if (referenceParameter.Type == SearchParamType.Reference
                                && referenceParameter.IsSupported
                                && referenceParameter.TargetResourceTypes != null
                                && referenceParameter.TargetResourceTypes.Contains(compartmentType, StringComparer.Ordinal))
                            {
                                AddCompartmentSearchParameter(referenceParameter, compartmentResourceType);
                            }
                        }
                    }
                }

                var searchParamAndResourceTypeExpressions = new List<Expression>();
                var finalCompartmentSearchExpressions = new List<Expression>();

                if (searchParameterInfoList.Any())
                {
                    foreach (var grouping in searchParameterInfoList)
                    {
                        // Always add the applicable resource types
                        Expression innerExpression = grouping.Value.ResourceTypes.Count > 1 ? Expression.In(FieldName.TokenCode, null, grouping.Value.ResourceTypes) : Expression.StringEquals(FieldName.TokenCode, null, grouping.Value.ResourceTypes.FirstOrDefault(), false);
                        SearchParameterExpression resourceTypesExpression = Expression.SearchParameter(
                            resourceTypeSearchParameter,
                            innerExpression);

                        searchParamAndResourceTypeExpressions.Add(Expression.SearchParameter(searchParameterInfoList[grouping.Key].searchParameterInfo, resourceTypesExpression));
                    }

                    if (searchParamAndResourceTypeExpressions.Any())
                    {
                        // Get the ORed expression of search parameter + resource type expressions
                        // Then AND with the compartment type and id to ensure we only get resources in the compartment
                        var oredExpression = Expression.Or(searchParamAndResourceTypeExpressions);
                        finalCompartmentSearchExpressions.Add(Expression.And(
                                oredExpression,
                                Expression.StringEquals(FieldName.ReferenceResourceType, null, compartmentType, false),
                                Expression.StringEquals(FieldName.ReferenceResourceId, null, compartmentId, false)));
                    }
                }
                else
                {
                    finalCompartmentSearchExpressions.Add(expression);
                }

                return finalCompartmentSearchExpressions;
            }
            else
            {
                throw new InvalidSearchOperationException(string.Format(Core.Resources.CompartmentTypeIsInvalid, compartmentType));
            }
        }
    }
}
