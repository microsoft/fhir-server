// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal sealed class InstantDateTimeRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        private readonly IModelInfoProvider _modelInfoProvider;

        public InstantDateTimeRewriter(IModelInfoProvider modelInfoProvider)
        {
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (!IsInstant(expression.Parameter))
            {
                return expression;
            }

            Expression predicate = expression.Expression.AcceptVisitor(EndFieldRewriter.Instance);
            return ReferenceEquals(predicate, expression.Expression)
                ? expression
                : new SearchParameterExpression(expression.Parameter, predicate);
        }

        public override Expression VisitTable(SearchParamTableExpression expression, object context)
        {
            Expression predicate = expression.Predicate?.AcceptVisitor(this, context);
            return ReferenceEquals(predicate, expression.Predicate)
                ? expression
                : new SearchParamTableExpression(expression.QueryGenerator, predicate, expression.Kind, expression.ChainLevel);
        }

        private bool IsInstant(SearchParameterInfo parameter)
        {
            // Custom definitions can leave old index rows and stale definition caches on other instances.
            if (!parameter.IsSystemDefined ||
                parameter.Type != SearchParamType.Date ||
                parameter.Component?.Count > 0 ||
                !PropertyPath.TryParse(parameter.Expression, out PropertyPath path) ||
                !_modelInfoProvider.IsKnownResource(path.ResourceType, ignoreCase: false))
            {
                return false;
            }

            IStructureDefinitionSummary summary = _modelInfoProvider.StructureDefinitionSummaryProvider.Provide(path.ResourceType);
            for (int i = 0; i < path.Properties.Count; i++)
            {
                if (summary == null)
                {
                    return false;
                }

                IElementDefinitionSummary[] matches = summary.GetElements()
                    .Where(element => string.Equals(element.ElementName, path.Properties[i], StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                if (matches.Length != 1 || matches[0].IsChoiceElement || matches[0].Type?.Length != 1)
                {
                    return false;
                }

                ITypeSerializationInfo type = matches[0].Type[0];
                if (i == path.Properties.Count - 1)
                {
                    return type is IStructureDefinitionReference reference &&
                        string.Equals(reference.ReferredType, "instant", StringComparison.Ordinal);
                }

                summary = type switch
                {
                    IStructureDefinitionSummary inlineSummary => inlineSummary,
                    IStructureDefinitionReference reference when !string.IsNullOrEmpty(reference.ReferredType) =>
                        _modelInfoProvider.StructureDefinitionSummaryProvider.Provide(reference.ReferredType),
                    _ => null,
                };
            }

            return false;
        }

        private sealed class PropertyPath
        {
            private PropertyPath(string resourceType, IReadOnlyList<string> properties)
            {
                ResourceType = resourceType;
                Properties = properties;
            }

            public string ResourceType { get; }

            public IReadOnlyList<string> Properties { get; }

            public static bool TryParse(string expression, out PropertyPath path)
            {
                path = null;
                if (string.IsNullOrEmpty(expression))
                {
                    return false;
                }

                string[] segments = expression.Split('.');
                if (segments.Length < 2 || segments.Any(segment =>
                    segment.Length == 0 || !IsLetter(segment[0]) ||
                    segment.Any(character => !IsLetter(character) && (character < '0' || character > '9'))))
                {
                    return false;
                }

                path = new PropertyPath(segments[0], Array.AsReadOnly(segments.Skip(1).ToArray()));
                return true;
            }

            private static bool IsLetter(char character) =>
                (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
        }

        private sealed class EndFieldRewriter : SqlExpressionRewriterWithInitialContext<object>
        {
            public static readonly EndFieldRewriter Instance = new EndFieldRewriter();

            public override Expression VisitBinary(BinaryExpression expression, object context) =>
                expression.FieldName == FieldName.DateTimeEnd
                    ? new BinaryExpression(expression.BinaryOperator, FieldName.DateTimeStart, expression.ComponentIndex, expression.Value)
                    : expression;

            public override Expression VisitSearchParameter(SearchParameterExpression expression, object context) => expression;

            public override Expression VisitChained(ChainedExpression expression, object context) => expression;

            public override Expression VisitSqlChainLink(SqlChainLinkExpression expression, object context) => expression;

            public override Expression VisitTable(SearchParamTableExpression expression, object context) => expression;
        }
    }
}
