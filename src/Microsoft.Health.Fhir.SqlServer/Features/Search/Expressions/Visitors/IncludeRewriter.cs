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

            List<TableExpression> reorderedExpressions;

            // TableExpressions contains at least one Include expression
            var nonIncludeExpressions = expression.TableExpressions.Where(e => e.Kind != TableExpressionKind.Include);
            var includeExpressions = expression.TableExpressions.Where(e => e.Kind == TableExpressionKind.Include);

            // Sort include expressions if there is an include iterate expression
            // Order so that include iterate expression appear after the expressions thet select from
            IEnumerable<TableExpression> sortedIncludeExpressions = includeExpressions;
            if (includeExpressions.Any(e => e.Kind == TableExpressionKind.Include && (e.NormalizedPredicate as IncludeExpression).Iterate))
            {
                sortedIncludeExpressions = IncludeExpressionTopologicalSort.Sort(includeExpressions);
            }

            // Add sorted include expressions after all other expressions
            reorderedExpressions = nonIncludeExpressions.Concat(sortedIncludeExpressions).ToList();

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

        private class IncludeExpressionTopologicalSort
        {
            // Based on Khan's algorithm. See https://en.wikipedia.org/wiki/Topological_sorting.
            // The search queries are acyclic.
            public static IList<TableExpression> Sort(IEnumerable<TableExpression> includeExpressions)
            {
                var graph = new IncludeExpressionDependencyGraph(includeExpressions);
                var sortedExpressions = new List<TableExpression>();

                while (graph.NodesWithoutIncomingEdges.Any())
                {
                    // Remove a node without incoming edges and add to the sorted list
                    var v = graph.NodesWithoutIncomingEdges.First();
                    sortedExpressions.Add(v);

                    graph.RemoveNodeAndAllOutgoingEdges(v);
                }

                return sortedExpressions;
            }

            // Dependency graph of parameters so that parameter b depends on a means that b includes from a, therefore a should appear before b.
            private class IncludeExpressionDependencyGraph
            {
                // private static readonly IncludeExpressionComparer Comparer = new IncludeExpressionComparer();
                public IncludeExpressionDependencyGraph(IEnumerable<TableExpression> includeExpressions)
                {
                    OutgoingEdges = new Dictionary<TableExpression, IList<TableExpression>>();
                    IncomingEdgesCount = new Dictionary<TableExpression, int>();

                    // Add graph nodes (parameters) and edges (dependencies)
                    foreach (var v in includeExpressions)
                    {
                        OutgoingEdges.Add(v, new List<TableExpression>());
                        IncomingEdgesCount.TryAdd(v, 0);

                        foreach (var u in includeExpressions)
                        {
                            // if (v != u && Comparer.Compare(v, u) < 0)
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

                // (x, y) is a graph edge if x needs to appear before y in the sorted query.
                // That is, y has dependency on x.
                private static bool IsDependencyEdge(TableExpression x, TableExpression y)
                {
                    if (x.Kind != TableExpressionKind.Include || y.Kind != TableExpressionKind.Include)
                    {
                        return false;
                    }

                    // Both expressions are Include expressions
                    var xInclude = (IncludeExpression)x.NormalizedPredicate;
                    var yInclude = (IncludeExpression)y.NormalizedPredicate;

                    // If x is :iterate and y is not :iterate, than there's no dependency.
                    if (xInclude.Iterate && !yInclude.Iterate)
                    {
                        return false;
                    }

                    // Order so that _include:iterate source type will appear after relevant include target
                    // and _revinclude:iterate target type appears after it's already been included
                    var xTargetTypes = xInclude.WildCard ? xInclude.ReferencedTypes : xInclude.ReferenceSearchParameter?.TargetResourceTypes;
                    var yTargetTypes = yInclude.WildCard ? yInclude.ReferencedTypes : yInclude.ReferenceSearchParameter?.TargetResourceTypes;

                    // Both are _revinclude or both are _include, order so that one's target resource type should appear after the other's source resource types if types are equal
                    if (xInclude.Reversed == yInclude.Reversed)
                    {
                        // x's target type matches y's source type => x > y
                        if ((xInclude.TargetResourceType != null && xInclude.TargetResourceType == yInclude.SourceResourceType)
                            || (xInclude.TargetResourceType == null && xTargetTypes != null && xTargetTypes.Contains(yInclude.SourceResourceType)))
                        {
                            return xInclude.Reversed ? false : true;
                        }

                        // y's target type matches x's source type => x < y
                        if ((yInclude.TargetResourceType != null && yInclude.TargetResourceType == xInclude.SourceResourceType)
                            || (yInclude.TargetResourceType == null && yTargetTypes != null && yTargetTypes.Contains(xInclude.SourceResourceType)))
                        {
                            return xInclude.Reversed ? true : false;
                        }
                    }

                    // x is _include and y is _revinclude
                    else if (!xInclude.Reversed && yInclude.Reversed)
                    {
                        // x's specified target type matches y's target type => x < y
                        if ((xInclude.TargetResourceType != null && yInclude.TargetResourceType != null && xInclude.TargetResourceType == yInclude.TargetResourceType)
                            || (xInclude.TargetResourceType != null && yTargetTypes != null && yTargetTypes.Contains(xInclude.TargetResourceType)))
                        {
                            return true;
                        }

                        // one of x's target types matches y's target type => x < y
                        if ((xInclude.TargetResourceType == null && xTargetTypes != null && yInclude.TargetResourceType != null && xTargetTypes.Contains(yInclude.TargetResourceType))
                            || (xInclude.TargetResourceType == null && xTargetTypes != null && yTargetTypes != null && xTargetTypes.Intersect(yTargetTypes).Any()))
                        {
                            return true;
                        }
                    }

                    // x is _revinclude and y is _include
                    else if (xInclude.Reversed && !yInclude.Reversed)
                    {
                        // x's source type matches y's source type
                        if (xInclude.SourceResourceType == yInclude.SourceResourceType)
                        {
                            return true;
                        }
                    }

                    return false;
                }
            }
        }
    }
}
