// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Rewrites CompartmentSearchExpression to use main search index.
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
            // 3 - any "uniersal" resources, such as Locations and Medications

            // First a collection of any resources which refer to the smart user
            // we use the CompartmentSearchRewriter to get this list as it matches what we want
            var expressionList = _compartmentSearchRewriter.BuildCompartmentSearchExpressionsGroup(expression);

            // Second the smart user's own resource
            expressionList.Add(
                Expression.SearchParameter(idSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, compartmentId, false)));

            // Finally we add in the "universal" resources, which are resources that are not compartment specific
            var universalResourceTypes = new List<string>()
            {
                KnownResourceTypes.Location,
                KnownResourceTypes.Organization,
                KnownResourceTypes.Practitioner,
                KnownResourceTypes.Medication,
            };

            var inExpression = Expression.In(FieldName.TokenCode, null, universalResourceTypes);

            expressionList.Add(Expression.SearchParameter(resourceTypeSearchParameter, inExpression));

            // new we union all those results together
            return Expression.Union(UnionOperator.All, expressionList);
        }
    }
}
