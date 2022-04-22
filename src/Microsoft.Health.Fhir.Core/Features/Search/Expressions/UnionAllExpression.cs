// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public class UnionAllExpression : Expression
    {
        public UnionAllExpression(IReadOnlyList<Expression> expressions)
        {
            EnsureArg.IsNotNull(expressions, nameof(expressions));
            EnsureArg.IsTrue(expressions.Any(), nameof(expressions));
            EnsureArg.IsTrue(expressions.All(o => o != null), nameof(expressions));

            Expressions = expressions;
        }

        public IReadOnlyList<Expression> Expressions { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitUnionAll(this, context);
        }

        public override string ToString()
        {
            return $"(UnionAll {Expressions} {string.Join(' ', Expressions)})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(UnionAllExpression));
            foreach (Expression expression in Expressions)
            {
                expression.AddValueInsensitiveHashCode(ref hashCode);
            }
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            if (other is not UnionAllExpression unionAll ||
                unionAll.Expressions.Count != Expressions.Count)
            {
                return false;
            }

            for (var i = 0; i < Expressions.Count; i++)
            {
                if (!unionAll.Expressions[i].ValueInsensitiveEquals(Expressions[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

// Plan:
//     - Create UnionAllExpression - Mix between MultiaryExpression and CompartmentSearchException?
//     - UnionAllExpression is not a multiary exception (keep following original SqlRootExpressionRewriter logic)
//     - Some additional changes in CosmosDB will be required
//     - Forces the creation  of multiple CTEs, but it'll also allow the aggregation of all them in the end
