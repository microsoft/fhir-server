// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal abstract class CompositeSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        private readonly NormalizedSearchParameterQueryGenerator[] _componentHandlers;

        protected CompositeSearchParameterQueryGenerator(params NormalizedSearchParameterQueryGenerator[] componentHandlers)
        {
            EnsureArg.IsNotNull(componentHandlers, nameof(componentHandlers));
            _componentHandlers = componentHandlers;
        }

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return expression.AcceptVisitor(_componentHandlers[(int)expression.ComponentIndex], context);
        }

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return expression.AcceptVisitor(_componentHandlers[(int)expression.ComponentIndex], context);
        }

        public override SearchParameterQueryGeneratorContext VisitMissingField(MissingFieldExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return expression.AcceptVisitor(_componentHandlers[(int)expression.ComponentIndex], context);
        }
    }
}
