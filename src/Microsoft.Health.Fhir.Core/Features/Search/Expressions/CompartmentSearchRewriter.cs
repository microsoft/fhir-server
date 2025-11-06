// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Rewrites CompartmentSearchExpression to use main search index.
    /// </summary>
    public class CompartmentSearchRewriter : ExpressionRewriterWithInitialContext<object>
    {
        private readonly Lazy<ICompartmentDefinitionManager> _compartmentDefinitionManager;
        private readonly Lazy<ISearchParameterDefinitionManager> _searchParameterDefinitionManager;

        public CompartmentSearchRewriter(Lazy<ICompartmentDefinitionManager> compartmentDefinitionManager, Lazy<ISearchParameterDefinitionManager> searchParameterDefinitionManager)
        {
            _compartmentDefinitionManager = EnsureArg.IsNotNull(compartmentDefinitionManager, nameof(compartmentDefinitionManager));
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
        }

        public override Expression VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            var compartmentSearchExpressionsGrouped = BuildCompartmentSearchExpressionsGroup(expression);
            return Expression.Union(UnionOperator.All, compartmentSearchExpressionsGrouped);
        }

        internal List<Expression> BuildCompartmentSearchExpressionsGroup(CompartmentSearchExpression expression)
        {
            SearchParameterInfo resourceTypeSearchParameter = _searchParameterDefinitionManager.Value.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);

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

                if (_compartmentDefinitionManager.Value.TryGetResourceTypes(parsedCompartmentType, out HashSet<string> resourceTypes))
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
                    if (_compartmentDefinitionManager.Value.TryGetSearchParams(compartmentResourceType, parsedCompartmentType, out HashSet<string> compartmentSearchParameters))
                    {
                        foreach (var compartmentSearchParameter in compartmentSearchParameters)
                        {
                            if (_searchParameterDefinitionManager.Value.TryGetSearchParameter(compartmentResourceType, compartmentSearchParameter, out SearchParameterInfo sp))
                            {
                                // Use the URL string as the key.
                                string searchParamUrl = sp.Url.ToString();

                                if (searchParameterInfoList.TryGetValue(searchParamUrl, out var info))
                                {
                                    // Add the compartment resource type if the key exists.
                                    info.ResourceTypes.Add(compartmentResourceType);
                                }
                                else
                                {
                                    // Otherwise, add a new dictionary entry.
                                    searchParameterInfoList[searchParamUrl] = (sp, new HashSet<string> { compartmentResourceType });
                                }
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
                        return finalCompartmentSearchExpressions;
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
