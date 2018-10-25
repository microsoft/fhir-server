// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public class SearchParameterExpression : SearchParameterExpressionBase
    {
        public SearchParameterExpression(string searchParameterName, IReadOnlyList<Expression> expressions)
            : base(searchParameterName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(searchParameterName, nameof(searchParameterName));
            EnsureArg.IsNotNull(expressions, nameof(expressions));
            EnsureArg.IsTrue(expressions.Any(), nameof(expressions));
            EnsureArg.IsTrue(expressions.All(o => o != null), nameof(expressions));

            Expressions = expressions;
        }

        public IReadOnlyList<Expression> Expressions { get; }

        protected internal override void AcceptVisitor(IExpressionVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            visitor.Visit(this);
        }
    }
}
