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

        private static readonly TableExpression IncludeUnionAllExpression = new TableExpression(null, null, null, TableExpressionKind.IncludeUnionAll);
        private static readonly TableExpression IncludeLimitExpression = new TableExpression(null, null, null, TableExpressionKind.IncludeLimit);

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 1 || expression.TableExpressions.All(e => e.Kind != TableExpressionKind.Include))
            {
                return expression;
            }

            // TableExpressions contains at least one Include expression
            var nonIncludeExpressions = expression.TableExpressions.Where(e => e.Kind != TableExpressionKind.Include).ToList();
            var includeExpressions = expression.TableExpressions.Where(e => e.Kind == TableExpressionKind.Include).ToList();

            // Sort include expressions if there is an include iterate expression
            // Order so that include iterate expression appear after the expressions they select from
            IEnumerable<TableExpression> sortedIncludeExpressions = includeExpressions;
            if (includeExpressions.Any(e => ((IncludeExpression)e.NormalizedPredicate).Iterate))
            {
                IEnumerable<TableExpression> nonIncludeIterateExpressions = includeExpressions.Where(e => !((IncludeExpression)e.NormalizedPredicate).Iterate);
                List<TableExpression> includeIterateExpressions = includeExpressions.Where(e => ((IncludeExpression)e.NormalizedPredicate).Iterate).ToList();
                sortedIncludeExpressions = nonIncludeIterateExpressions.Concat(SortIncludeIterateExpressions(includeIterateExpressions));
            }

            // Add sorted include expressions after all other expressions
            var reorderedExpressions = nonIncludeExpressions.Concat(sortedIncludeExpressions).ToList();

            // We are adding an extra CTE after each include cte, so we traverse the ordered
            // list from the end and add a limit expression after each include expression
            for (var i = reorderedExpressions.Count - 1; i >= 0; i--)
            {
                switch (reorderedExpressions[i].SearchParameterQueryGenerator)
                {
                    case IncludeQueryGenerator _:
                        reorderedExpressions.Insert(i + 1, IncludeLimitExpression);
                        break;
                    default:
                        break;
                }
            }

            reorderedExpressions.Add(IncludeUnionAllExpression);
            return new SqlRootExpression(reorderedExpressions, expression.DenormalizedExpressions);
        }

        private static IList<TableExpression> SortIncludeIterateExpressions(IList<TableExpression> expressions)
        {
            // Based on Khan's algorithm. See https://en.wikipedia.org/wiki/Topological_sorting.
            // The search queries are acyclic.
            if (expressions.Count == 1)
            {
                return expressions;
            }

            var graph = new IncludeIterateExpressionDependencyGraph(expressions);
            var sortedExpressions = new List<TableExpression>();

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
            public IncludeIterateExpressionDependencyGraph(IEnumerable<TableExpression> includeIterateExpressions)
            {
                OutgoingEdges = new Dictionary<TableExpression, IList<TableExpression>>();
                IncomingEdgesCount = new Dictionary<TableExpression, int>();

                // Add graph nodes (parameters) and edges (dependencies)
                foreach (var v in includeIterateExpressions)
                {
                    OutgoingEdges.Add(v, new List<TableExpression>());
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

            public IDictionary<TableExpression, IList<TableExpression>> OutgoingEdges { get; private set; }

            public IDictionary<TableExpression, int> IncomingEdgesCount { get; private set; }

            public IEnumerable<TableExpression> NodesWithoutIncomingEdges
            {
                get { return IncomingEdgesCount.Where(e => e.Value == 0).Select(e => e.Key); }
            }

            // Remove v and all v's edges and update incoming edge count accordingly
            public void RemoveNodeAndAllOutgoingEdges(TableExpression v)
            {
                if (OutgoingEdges.ContainsKey(v))
                {
                    // Remove all edges
                    IList<TableExpression> edges;
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
            private static bool IsDependencyEdge(TableExpression x, TableExpression y)
            {
                // Assumes both expressions are include iterate expressions
                var xInclude = (IncludeExpression)x.NormalizedPredicate;
                var yInclude = (IncludeExpression)y.NormalizedPredicate;

                return xInclude.Produces.Intersect(yInclude.Requires).Any();
            }
        }
    }
}
