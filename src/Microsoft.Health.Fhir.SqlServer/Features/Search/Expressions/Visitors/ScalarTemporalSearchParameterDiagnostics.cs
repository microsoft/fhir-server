// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal static class ScalarTemporalSearchParameterDiagnostics
    {
        internal static IReadOnlyList<ScalarTemporalSearchParameterDiagnostic> Collect(Expression expression)
        {
            var context = new List<ScalarTemporalSearchParameterDiagnostic>();
            expression?.AcceptVisitor(Collector.Instance, context);
            return context;
        }

        internal static string BuildSummary(IReadOnlyList<ScalarTemporalSearchParameterDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return "none";
            }

            return string.Join(
                ";",
                diagnostics.Select(x =>
                    $"url={x.Url ?? string.Empty},code={x.Code ?? string.Empty},scalarTemporal={x.IsScalarTemporal},allowListed={x.IsAllowListed},equality={x.HasEqualityShape},wouldRewrite={x.WouldRewrite}"));
        }

        internal readonly struct ScalarTemporalSearchParameterDiagnostic
        {
            public ScalarTemporalSearchParameterDiagnostic(string url, string code, bool isScalarTemporal, bool isAllowListed, bool hasEqualityShape, bool wouldRewrite)
            {
                Url = url;
                Code = code;
                IsScalarTemporal = isScalarTemporal;
                IsAllowListed = isAllowListed;
                HasEqualityShape = hasEqualityShape;
                WouldRewrite = wouldRewrite;
            }

            public string Url { get; }

            public string Code { get; }

            public bool IsScalarTemporal { get; }

            public bool IsAllowListed { get; }

            public bool HasEqualityShape { get; }

            public bool WouldRewrite { get; }
        }

        private sealed class Collector : ExpressionRewriterWithInitialContext<List<ScalarTemporalSearchParameterDiagnostic>>
        {
            internal static readonly Collector Instance = new Collector();

            public override Expression VisitSearchParameter(SearchParameterExpression expression, List<ScalarTemporalSearchParameterDiagnostic> context)
            {
                if (expression.Parameter?.IsScalarTemporal == true)
                {
                    bool isAllowListed = ScalarTemporalEqualityRewriter.IsActivatedScalarTemporalParameter(expression);
                    bool hasEqualityShape = ScalarTemporalEqualityRewriter.TryMatchEqualityPattern(expression.Expression, out BinaryExpression startGe, out BinaryExpression endLe);
                    bool wouldRewrite = isAllowListed &&
                        hasEqualityShape &&
                        startGe.Value is DateTimeOffset startValue &&
                        endLe.Value is DateTimeOffset endValue &&
                        ScalarTemporalEqualityRewriter.IsRewritablePrecision(startValue, endValue);

                    context.Add(new ScalarTemporalSearchParameterDiagnostic(
                        expression.Parameter.Url?.OriginalString,
                        expression.Parameter.Code,
                        expression.Parameter.IsScalarTemporal,
                        isAllowListed,
                        hasEqualityShape,
                        wouldRewrite));
                }

                return base.VisitSearchParameter(expression, context);
            }
        }
    }
}
