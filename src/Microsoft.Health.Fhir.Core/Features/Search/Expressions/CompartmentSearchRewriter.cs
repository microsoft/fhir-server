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
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Rewrites CompartmentSearchExpression to use main search index.
    /// </summary>
    public abstract class CompartmentSearchRewriter : ExpressionRewriterWithInitialContext<object>
    {
        private readonly Lazy<ICompartmentDefinitionManager> _compartmentDefinitionManager;
        private readonly Lazy<ISearchParameterDefinitionManager> _searchParameterDefinitionManager;

        protected CompartmentSearchRewriter(Lazy<ICompartmentDefinitionManager> compartmentDefinitionManager, Lazy<ISearchParameterDefinitionManager> searchParameterDefinitionManager)
        {
            _compartmentDefinitionManager = EnsureArg.IsNotNull(compartmentDefinitionManager, nameof(compartmentDefinitionManager));
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
        }

        // Add protected properties for derived class access
        protected Lazy<ICompartmentDefinitionManager> CompartmentDefinitionManager => _compartmentDefinitionManager;

        protected Lazy<ISearchParameterDefinitionManager> SearchParameterDefinitionManager => _searchParameterDefinitionManager;

        public override Expression VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            var expressions = BuildCompartmentSearchExpressionsGroup(expression);

            // During SQL query generation, UnionExpressions are pulled at the top so always create union expression here.
            return Expression.Union(UnionOperator.All, expressions.ToList());
        }

        // Abstract method - must be implemented by SQL/Cosmos versions
        public abstract IReadOnlyCollection<Expression> BuildCompartmentSearchExpressionsGroup(
            CompartmentSearchExpression expression);
    }
}
