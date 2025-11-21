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
    /// Builds on the CompartmentSearchRewriter to add additional resources for Smart access
    /// </summary>
    public class SmartCompartmentSearchRewriter : ExpressionRewriterWithInitialContext<object>
    {
        private readonly Lazy<ISearchParameterDefinitionManager> _searchParameterDefinitionManager;
        private readonly CompartmentSearchRewriter _compartmentSearchRewriter;

        public SmartCompartmentSearchRewriter(CompartmentSearchRewriter compartmentSearchRewriter, Lazy<ISearchParameterDefinitionManager> searchParameterDefinitionManager)
        {
            _compartmentSearchRewriter = EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
        }

        public override Expression VisitSmartCompartment(SmartCompartmentSearchExpression expression, object context)
        {
            SearchParameterInfo resourceTypeSearchParameter = _searchParameterDefinitionManager.Value.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType);
            SearchParameterInfo idSearchParameter = _searchParameterDefinitionManager.Value.GetSearchParameter(expression.CompartmentType, SearchParameterNames.Id);

            var compartmentType = expression.CompartmentType;
            var compartmentId = expression.CompartmentId;

            // A smart user compartment is used to filter all search results by the resources available to the smart user
            // The smart user has access to 3 things:
            // 1 - any resource which refers to them
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

            // Finally we add in the "universal" resources, which are resources that are not compartment specific
            var universalResourceTypes = new List<string>()
            {
                KnownResourceTypes.Location,
                KnownResourceTypes.Organization,
                KnownResourceTypes.Practitioner,
                KnownResourceTypes.Medication,
                KnownCompartmentTypes.Device,
            };

            // In case FilteredResourceTypes is specified and not the default, we need to filter down the universalResourceTypes to only those specified
            if (expression.FilteredResourceTypes.Any(resourceType => !string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.Ordinal)))
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

            // union all those results together
            return Expression.Union(UnionOperator.All, expressionList);
        }
    }
}
