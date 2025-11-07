// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Rewrites CosmosCompartmentSearchRewriter to use main search index.
    /// </summary>
    public class CosmosCompartmentSearchRewriter : CompartmentSearchRewriter
    {
        public CosmosCompartmentSearchRewriter(
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
                var compartmentSearchExpressions = new Dictionary<string, (SearchParameterExpression Expression, HashSet<string> ResourceTypes)>();

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
                    var searchParamExpressionsForResourceType = new List<SearchParameterExpression>();
                    if (CompartmentDefinitionManager.Value.TryGetSearchParams(compartmentResourceType, parsedCompartmentType, out HashSet<string> compartmentSearchParameters))
                    {
                        foreach (var compartmentSearchParameter in compartmentSearchParameters)
                        {
                            if (SearchParameterDefinitionManager.Value.TryGetSearchParameter(compartmentResourceType, compartmentSearchParameter, out SearchParameterInfo sp))
                            {
                                searchParamExpressionsForResourceType.Add(
                                    Expression.SearchParameter(sp, Expression.And(Expression.StringEquals(FieldName.ReferenceResourceType, null, compartmentType, false), Expression.StringEquals(FieldName.ReferenceResourceId, null, compartmentId, false))));
                            }
                        }
                    }

                    foreach (var expr in searchParamExpressionsForResourceType)
                    {
                        string searchParamUrl = expr.Parameter.Url.ToString();
                        if (compartmentSearchExpressions.TryGetValue(searchParamUrl, out var resourceTypeList))
                        {
                            resourceTypeList.ResourceTypes.Add(compartmentResourceType);
                        }
                        else
                        {
                            compartmentSearchExpressions[searchParamUrl] = (expr, new HashSet<string> { compartmentResourceType });
                        }
                    }
                }

                var compartmentSearchExpressionsGrouped = new List<Expression>();

                if (compartmentSearchExpressions.Any())
                {
                    foreach (var grouping in compartmentSearchExpressions)
                    {
                        // When we're searching more than 1 compartment resource type (i.e. Patient/abc/*) the search parameters need to list the applicable resource types
                        if (compartmentResourceTypesToSearch.Count > 1)
                        {
                            var inExpression = Expression.In(FieldName.TokenCode, null, grouping.Value.ResourceTypes);

                            SearchParameterExpression resourceTypesExpression = Expression.SearchParameter(
                                resourceTypeSearchParameter,
                                inExpression);

                            compartmentSearchExpressionsGrouped.Add(Expression.And(grouping.Value.Expression, resourceTypesExpression));
                        }
                        else
                        {
                            compartmentSearchExpressionsGrouped.Add(grouping.Value.Expression);
                        }
                    }
                }
                else
                {
                    compartmentSearchExpressionsGrouped.Add(expression);
                }

                return compartmentSearchExpressionsGrouped;
            }
            else
            {
                throw new InvalidSearchOperationException(string.Format(Core.Resources.CompartmentTypeIsInvalid, compartmentType));
            }
        }
    }
}
