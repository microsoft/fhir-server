// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// The root of a search expression tree that will be translated to a SQL batch.
    /// It is organized as a set of expressions that are over search parameter tables (<see cref="SearchParamTableExpressions"/>) and a set
    /// of expressions over the columns on the Resource table (<see cref="ResourceTableExpressions"/>).
    /// </summary>
    internal class SqlRootExpression : Expression
    {
        public SqlRootExpression(IReadOnlyList<SearchParamTableExpression> searchParamTableExpressions, IReadOnlyList<SearchParameterExpressionBase> resourceTableExpressions)
        {
            EnsureArg.IsNotNull(searchParamTableExpressions, nameof(searchParamTableExpressions));
            EnsureArg.IsNotNull(resourceTableExpressions, nameof(resourceTableExpressions));

            SearchParamTableExpressions = searchParamTableExpressions;
            ResourceTableExpressions = resourceTableExpressions;
        }

        /// <summary>
        /// Expressions applied to various search parameter tables (e.g. TokenSearchParam, NumberSearchParam, etc.) or the CompartmentAssignment table.
        /// </summary>
        public IReadOnlyList<SearchParamTableExpression> SearchParamTableExpressions { get; }

        /// <summary>
        /// Expressions applied to directly to the Resource table.
        /// </summary>
        public IReadOnlyList<SearchParameterExpressionBase> ResourceTableExpressions { get; }

        public static SqlRootExpression WithSearchParamTableExpressions(params SearchParamTableExpression[] expressions)
        {
            return new SqlRootExpression(expressions, Array.Empty<SearchParameterExpressionBase>());
        }

        public static SqlRootExpression WithResourceTableExpressions(params SearchParameterExpressionBase[] expressions)
        {
            return new SqlRootExpression(Array.Empty<SearchParamTableExpression>(), expressions);
        }

        public static SqlRootExpression WithSearchParamTableExpressions(IReadOnlyList<SearchParamTableExpression> expressions)
        {
            return new SqlRootExpression(expressions, Array.Empty<SearchParameterExpressionBase>());
        }

        public static SqlRootExpression WithResourceTableExpressions(IReadOnlyList<SearchParameterExpressionBase> expressions)
        {
            return new SqlRootExpression(Array.Empty<SearchParamTableExpression>(), expressions);
        }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return AcceptVisitor((ISqlExpressionVisitor<TContext, TOutput>)visitor, context);
        }

        public TOutput AcceptVisitor<TContext, TOutput>(ISqlExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitSqlRoot(this, context);
        }

        public override string ToString()
        {
            return $"(SqlRoot (SearchParamTables:{(SearchParamTableExpressions.Any() ? " " + string.Join(" ", SearchParamTableExpressions) : null)}) (ResourceTable:{(ResourceTableExpressions.Any() ? " " + string.Join(" ", ResourceTableExpressions) : null)}))";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(SqlRootExpression));
            foreach (SearchParamTableExpression searchParamTableExpression in SearchParamTableExpressions)
            {
                hashCode.Add(searchParamTableExpression);
            }

            foreach (SearchParameterExpressionBase resourceTableExpression in ResourceTableExpressions)
            {
                hashCode.Add(resourceTableExpression);
            }
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            if (other is not SqlRootExpression sqlRoot ||
                sqlRoot.ResourceTableExpressions.Count != ResourceTableExpressions.Count ||
                sqlRoot.SearchParamTableExpressions.Count != SearchParamTableExpressions.Count)
            {
                return false;
            }

            for (var i = 0; i < ResourceTableExpressions.Count; i++)
            {
                if (!sqlRoot.ResourceTableExpressions[i].ValueInsensitiveEquals(ResourceTableExpressions[i]))
                {
                    return false;
                }
            }

            for (var i = 0; i < SearchParamTableExpressions.Count; i++)
            {
                if (!sqlRoot.SearchParamTableExpressions[i].ValueInsensitiveEquals(SearchParamTableExpressions[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
