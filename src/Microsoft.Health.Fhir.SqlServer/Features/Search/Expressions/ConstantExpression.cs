// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    internal class ConstantExpression : Expression
    {
        public ConstantExpression(object value)
        {
            Value = value;
        }

        public object Value { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            throw new System.NotImplementedException();
        }

        public override string ToString()
        {
            return $"(Constant {(Value is null ? "NULL" : Value is string ? $"'{Value}'" : Value)})";
        }
    }
}
