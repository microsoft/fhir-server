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
    public class NotReferencedExpression : Expression
    {
        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
        }

        public override string ToString()
        {
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
        }
    }
}
