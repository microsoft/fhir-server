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

            List<TableExpression> reorderedExpressions = expression.TableExpressions.OrderByDescending(t => t, new TableExpressionComparer()).ToList();

            bool containsInclude = expression.TableExpressions.AsEnumerable().Where(e => e.SearchParameterQueryGenerator is IncludeQueryGenerator).Any();

            if (containsInclude)
            {
                // Remove 'orphan' include expressions which has no result set to include from.
                // For example, remove the _include:iterate search parameter from the following query
                // MedicationDispense?_include=MedicationDispense:prescription&_include:iterate=Patient:general-practitioner)
                // RemoveOrphanIncludeExpressions(reorderedExpressions);

                // We are adding an extra CTE after each include cte (except recursive :iterate), so we traverse the ordered
                // list from the end and add a limit expression after each include expression
                AddIncludeLimitExpressions(reorderedExpressions);
            }

            // Check if after removing orphan includes there are still include expressions
            containsInclude = reorderedExpressions.Where(e => e.SearchParameterQueryGenerator is IncludeQueryGenerator).Any();

            if (containsInclude)
            {
                reorderedExpressions.Add(IncludeUnionAllExpression);
            }

            return new SqlRootExpression(reorderedExpressions, expression.DenormalizedExpressions);
        }

        private void RemoveOrphanIncludeExpressions(List<TableExpression> reorderedExpressions)
        {
            var targetTypes = new List<string>();
            var expressionsToRemove = new List<TableExpression>();

            foreach (var te in reorderedExpressions)
            {
                switch (te.Kind)
                {
                    case TableExpressionKind.Include:
                        var includeExpression = (IncludeExpression)te.NormalizedPredicate;

                        // TODO (limorl) Handle WildCard & RevInclude
                        if (includeExpression.WildCard || includeExpression.Reversed)
                        {
                            break;
                        }

                        // if (targetTypes.Intersect(includeExpression.ReferenceSearchParameter?.BaseResourceTypes.ToList()).Any())
                        if (targetTypes.Contains(includeExpression.ResourceType))
                        {
                            // Not an orphan include
                            var types = !string.IsNullOrEmpty(includeExpression.TargetResourceType) ? new List<string> { includeExpression.TargetResourceType } : includeExpression.ReferenceSearchParameter.TargetResourceTypes.ToList();
                            targetTypes.AddRange(types);
                        }
                        else
                        {
                            expressionsToRemove.Add(te);
                        }

                        break;
                    case TableExpressionKind.Normal:
                    case TableExpressionKind.All:
                        // Insert the main resource type to target types
                        if (te.DenormalizedPredicate is SearchParameterExpression)
                        {
                            var searchParameterExpression = (SearchParameterExpression)te.DenormalizedPredicate;
                            targetTypes.Add((searchParameterExpression.Expression as StringExpression).Value);
                        }
                        else if (te.DenormalizedPredicate is MultiaryExpression)
                        {
                            var multiExpression = (MultiaryExpression)te.DenormalizedPredicate;
                            multiExpression.Expressions?.Where(e => e is SearchParameterExpression && (e as SearchParameterExpression).Parameter.Name == SearchParameterNames.ResourceType)
                                                        .Select(e => (e as SearchParameterExpression).Expression as StringExpression)
                                                        .ToList()
                                                        .ForEach(se => targetTypes.Add(se.Value));
                        }

                        break;
                    default:
                        break;
                }
            }

            expressionsToRemove.All(te => reorderedExpressions.Remove(te));

            /*var includeExpressions = reorderedExpressions?
                                    .Where(e => e.SearchParameterQueryGenerator is IncludeQueryGenerator)
                                    .Select(e => (IncludeExpression)e.NormalizedPredicate);

            foreach (var ie in includeExpressions)
            {
                var types = !string.IsNullOrEmpty(ie.TargetResourceType) ? new List<string> { ie.TargetResourceType } : ie.ReferenceSearchParameter.TargetResourceTypes.ToList();
                targetTypes.AddRange(types);
            }

            // Make sure include expression has a result set to select from, otherwise remove the expression
            reorderedExpressions.RemoveAll(te => te.SearchParameterQueryGenerator is IncludeQueryGenerator && !targetTypes.Contains(((IncludeExpression)te.NormalizedPredicate).ResourceType));

            for (var i = reorderedExpressions.Count - 1; i >= 0; i--)
            {
                if (reorderedExpressions[i].SearchParameterQueryGenerator is IncludeQueryGenerator)
                {
                    var includeExpression = (IncludeExpression)reorderedExpressions[i].NormalizedPredicate;
                    if (!targetTypes.Contains(includeExpression.ResourceType))
                    {
                        reorderedExpressions.Remove(reorderedExpressions[i]);
                    }
                }
            }*/
        }

        private void AddIncludeLimitExpressions(List<TableExpression> reorderedExpressions)
        {
            for (var i = reorderedExpressions.Count - 1; i >= 0; i--)
            {
                switch (reorderedExpressions[i].SearchParameterQueryGenerator)
                {
                    case IncludeQueryGenerator _:
                        var includeExpression = (IncludeExpression)reorderedExpressions[i].NormalizedPredicate;
                        if (!includeExpression.Recursive)
                        {
                            reorderedExpressions.Insert(i + 1, IncludeLimitExpression);
                        }

                        break;
                    default:
                        break;
                }
            }
        }

        private class TableExpressionComparer : IComparer<TableExpression>
        {
            public int Compare(TableExpression x, TableExpression y)
            {
                if (x.SearchParameterQueryGenerator is IncludeQueryGenerator)
                {
                    if (!(y.SearchParameterQueryGenerator is IncludeQueryGenerator))
                    {
                        return -1;
                    }

                    // Both expressions are Include expressions
                    var xInclude = (IncludeExpression)x.NormalizedPredicate;
                    var yInclude = (IncludeExpression)y.NormalizedPredicate;

                    if (!xInclude.Iterate && yInclude.Iterate)
                    {
                        return 1;
                    }

                    if (xInclude.Iterate && !yInclude.Iterate)
                    {
                        return -1;
                    }

                    // Both expressions are Include:iterate expressions, order so that _include:iterate source type will appear after relevant include target
                    var xTargetTypes = !string.IsNullOrEmpty(xInclude.TargetResourceType) ? new List<string>() { xInclude.TargetResourceType } : xInclude.ReferenceSearchParameter.TargetResourceTypes;

                    if (xTargetTypes.Contains(yInclude.ResourceType))
                    {
                        return 1;
                    }

                    return 0;
                }
                else if (!(y.SearchParameterQueryGenerator is IncludeQueryGenerator))
                {
                    return 0;
                }

                return 1;
            }
        }
    }
}
