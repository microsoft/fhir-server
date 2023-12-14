// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewriter used to put the include expressions at the end of the list of table expressions.
    /// </summary>
    internal class IncludeRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly IncludeRewriter Instance = new IncludeRewriter();

        private static readonly SearchParamTableExpression IncludeUnionAllExpression = new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeUnionAll);
        private static readonly SearchParamTableExpression IncludeLimitExpression = new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.IncludeLimit);

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.SearchParamTableExpressions.Count == 1 || expression.SearchParamTableExpressions.All(e => e.Kind != SearchParamTableExpressionKind.Include))
            {
                return expression;
            }

            // SearchParamTableExpressions contains at least one Include expression
            var nonIncludeExpressions = expression.SearchParamTableExpressions.Where(e => e.Kind != SearchParamTableExpressionKind.Include).ToList();
            var includeExpressions = expression.SearchParamTableExpressions.Where(e => e.Kind == SearchParamTableExpressionKind.Include).ToList();

            // Sort include expressions if there is an include iterate expression
            // Order so that include iterate expression appear after the expressions they select from
            IEnumerable<SearchParamTableExpression> sortedIncludeExpressions = includeExpressions;
            if (includeExpressions.Any(e => ((IncludeExpression)e.Predicate).Iterate))
            {
                IEnumerable<SearchParamTableExpression> nonIncludeIterateExpressions = includeExpressions.Where(e => !((IncludeExpression)e.Predicate).Iterate);
                List<SearchParamTableExpression> includeIterateExpressions = includeExpressions.Where(e => ((IncludeExpression)e.Predicate).Iterate).ToList();
                sortedIncludeExpressions = nonIncludeIterateExpressions.Concat(SortIncludeIterateExpressions(includeIterateExpressions));
            }

            // Add sorted include expressions after all other expressions
            var reorderedExpressions = nonIncludeExpressions.Concat(sortedIncludeExpressions).ToList();

            // We are adding an extra CTE after each include cte, so we traverse the ordered
            // list from the end and add a limit expression after each include expression
            for (var i = reorderedExpressions.Count - 1; i >= 0; i--)
            {
                switch (reorderedExpressions[i].QueryGenerator)
                {
                    case IncludeQueryGenerator _:
                        reorderedExpressions.Insert(i + 1, IncludeLimitExpression);
                        break;
                    default:
                        break;
                }
            }

            reorderedExpressions.Add(IncludeUnionAllExpression);
            return new SqlRootExpression(reorderedExpressions, expression.ResourceTableExpressions);
        }

        private static IList<SearchParamTableExpression> SortIncludeIterateExpressions(IList<SearchParamTableExpression> expressions)
        {
            // Based on Khan's algorithm. See https://en.wikipedia.org/wiki/Topological_sorting.
            // The search queries are acyclic.
            if (expressions.Count == 1)
            {
                return expressions;
            }

            var graph = new IncludeIterateExpressionDependencyGraph(expressions);
            var sortedExpressions = new List<SearchParamTableExpression>();

            while (graph.NodesWithoutIncomingEdges.Any())
            {
                // Remove a node without incoming edges and add to the sorted list
                var v = graph.NodesWithoutIncomingEdges.First();
                sortedExpressions.Add(v);

                graph.RemoveNodeAndAllOutgoingEdges(v);
            }

            // If there are edges, then the graph contains a cycle
            if (graph.OutgoingEdges.Any())
            {
                throw new SearchOperationNotSupportedException(Resources.CyclicIncludeIterateNotSupported);
            }

            return sortedExpressions;
        }

        // Dependency graph of parameters so that parameter b depends on a means that b includes from a, therefore a should appear before b.
        private class IncludeIterateExpressionDependencyGraph
        {
            // private static readonly IncludeExpressionComparer Comparer = new IncludeExpressionComparer();
            public IncludeIterateExpressionDependencyGraph(IEnumerable<SearchParamTableExpression> includeIterateExpressions)
            {
                OutgoingEdges = new Dictionary<SearchParamTableExpression, IList<SearchParamTableExpression>>();
                IncomingEdgesCount = new Dictionary<SearchParamTableExpression, int>();
                includeIterateExpressions = includeIterateExpressions.ToList();

                // Add graph nodes (parameters) and edges (dependencies)
                foreach (var v in includeIterateExpressions)
                {
                    OutgoingEdges.Add(v, new List<SearchParamTableExpression>());
                    IncomingEdgesCount.TryAdd(v, 0);

                    foreach (var u in includeIterateExpressions)
                    {
                        if (v != u && IsDependencyEdge(v, u))
                        {
                            IncomingEdgesCount.TryAdd(u, 0);
                            OutgoingEdges[v].Add(u);
                            IncomingEdgesCount[u]++;
                        }
                    }
                }
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "This is a public signature.")]
            public IDictionary<SearchParamTableExpression, IList<SearchParamTableExpression>> OutgoingEdges { get; private set; }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "This is a public signature.")]
            public IDictionary<SearchParamTableExpression, int> IncomingEdgesCount { get; private set; }

            public IEnumerable<SearchParamTableExpression> NodesWithoutIncomingEdges
            {
                get { return IncomingEdgesCount.Where(e => e.Value == 0).Select(e => e.Key); }
            }

            // Remove v and all v's edges and update incoming edge count accordingly
            public void RemoveNodeAndAllOutgoingEdges(SearchParamTableExpression v)
            {
                if (OutgoingEdges.ContainsKey(v))
                {
                    // Remove all edges
                    IList<SearchParamTableExpression> edges;
                    if (OutgoingEdges.TryGetValue(v, out edges))
                    {
                        while (edges.Any())
                        {
                            var u = edges[0];
                            edges.RemoveAt(0);
                            IncomingEdgesCount[u]--;
                        }
                    }

                    // Remove node
                    OutgoingEdges.Remove(v);
                    IncomingEdgesCount.Remove(v);
                }
            }

            // (x, y) is a graph edge if x needs to appear before y in the sorted query. That is, y has dependency on x.
            private static bool IsDependencyEdge(SearchParamTableExpression x, SearchParamTableExpression y)
            {
                // Assumes both expressions are include iterate expressions
                var xInclude = (IncludeExpression)x.Predicate;
                var yInclude = (IncludeExpression)y.Predicate;

                return xInclude.Produces.Intersect(yInclude.Requires).Any();
            }
        }
    }
}
