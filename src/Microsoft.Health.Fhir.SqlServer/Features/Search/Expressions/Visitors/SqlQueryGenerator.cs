// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class SqlQueryGenerator : ISqlExpressionVisitor<object, object>
    {
        private readonly StringBuilder _sb;
        private int _tableExpressionCounter;

        public SqlQueryGenerator(StringBuilder sb)
        {
            EnsureArg.IsNotNull(sb, nameof(sb));
            _sb = sb;
        }

        public object VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.NormalizedPredicates.Count > 0)
            {
                _sb.Append("WITH ").Append(TableExpressionName(_tableExpressionCounter++)).AppendLine(" AS")
                    .AppendLine("(");

                // do stuff

                _sb.AppendLine(")");
            }

            throw new System.NotImplementedException();
        }

        private string TableExpressionName(int id)
        {
            return "e" + id;
        }

        public object VisitTable(TableExpression tableExpression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitBinary(BinaryExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitChained(ChainedExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitMissingField(MissingFieldExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitMissingSearchParameter(MissingSearchParameterExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitMultiary(MultiaryExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitString(StringExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }

        public object VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            throw new System.NotImplementedException();
        }
    }
}
