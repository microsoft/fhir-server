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

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Handles searching for resources that have no references to them.
    /// Currently this only supports searching for resources that are have no references to them, but in the future could be enhanced to look for resources that don't have specific references to them.
    /// </summary>
    public class NotReferencedExpression : Expression
    {
        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitNotReferenced(this, context);
        }

        public override string ToString()
        {
            return "NotReferenced";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(NotReferencedExpression));
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is NotReferencedExpression;
        }
    }
}
