// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Expression = Hl7.FhirPath.Expressions.Expression;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public class SearchParameterComparer : ISearchParameterComparer
    {
        private readonly FhirPathCompiler _compiler;
        private readonly ILogger<ISearchParameterComparer> _logger;

        public SearchParameterComparer(ILogger<ISearchParameterComparer> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _compiler = new FhirPathCompiler();
            _logger = logger;
        }

        public bool Compare(SearchParameterInfo x, SearchParameterInfo y)
        {
            EnsureArg.IsNotNull(x, nameof(x));
            EnsureArg.IsNotNull(y, nameof(y));

            if (!string.Equals(x.Url.OriginalString, y.Url.OriginalString, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Url mismatch: '{x.Url}', '{x.Url}'");
                return false;
            }

            if (!string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Code mismatch: '{x.Code}', '{x.Code}'");
                return false;
            }

            if (x.Type != y.Type)
            {
                _logger.LogInformation($"Type mismatch: '{x.Type}', '{x.Type}'");
                return false;
            }

            if (x.Type == ValueSets.SearchParamType.Composite)
            {
                var componentX = x.Component.Select<SearchParameterComponentInfo, (string, string)>(x => new(x.DefinitionUrl.OriginalString, x.Expression)).ToList();
                var componentY = y.Component.Select<SearchParameterComponentInfo, (string, string)>(x => new(x.DefinitionUrl.OriginalString, x.Expression)).ToList();
                if (!CompareComponent(componentY, componentX))
                {
                    _logger.LogInformation($"Component mismatch: '{componentX.Count} components', '{componentY.Count} components'");
                    return false;
                }
            }

            var baseX = x.BaseResourceTypes?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
            var baseY = y.BaseResourceTypes?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
            if (CompareBase(baseX, baseY) != 0)
            {
                _logger.LogInformation($"Base mismatch: '{string.Join(",", baseX)}', '{string.Join(",", baseY)}'");
                return false;
            }

            if (CompareExpression(x.Expression, y.Expression) != 0)
            {
                _logger.LogInformation($"Expression mismatch: '{x.Expression}', '{y.Expression}'");
                return false;
            }

            return true;
        }

        public bool Compare(SearchParameter x, SearchParameter y)
        {
            EnsureArg.IsNotNull(x, nameof(x));
            EnsureArg.IsNotNull(y, nameof(y));

            if (!string.Equals(x.Url, y.Url, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Url mismatch: '{x.Url}', '{x.Url}'");
                return false;
            }

            if (!string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Code mismatch: '{x.Code}', '{x.Code}'");
                return false;
            }

            if (x.Type != y.Type)
            {
                _logger.LogInformation($"Type mismatch: '{x.Type}', '{x.Type}'");
                return false;
            }

            if (x.Type == SearchParamType.Composite)
            {
                var componentX = x.Component.Select<SearchParameter.ComponentComponent, (string, string)>(x => new(x.GetComponentDefinitionUri().OriginalString, x.Expression)).ToList();
                var componentY = y.Component.Select<SearchParameter.ComponentComponent, (string, string)>(x => new(x.GetComponentDefinitionUri().OriginalString, x.Expression)).ToList();
                if (!CompareComponent(componentY, componentX))
                {
                    _logger.LogInformation($"Component mismatch: '{componentX.Count} components', '{componentY.Count} components'");
                    return false;
                }
            }

            var baseX = x.Base?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
            var baseY = y.Base?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
            if (CompareBase(baseX, baseY) != 0)
            {
                _logger.LogInformation($"Base mismatch: '{string.Join(",", baseX)}', '{string.Join(",", baseY)}'");
                return false;
            }

            if (CompareExpression(x.Expression, y.Expression) != 0)
            {
                _logger.LogInformation($"Expression mismatch: '{x.Expression}', '{y.Expression}'");
                return false;
            }

            return true;
        }

        public int CompareBase(IEnumerable<string> x, IEnumerable<string> y)
        {
            EnsureArg.IsNotNull(x, nameof(x));
            EnsureArg.IsNotNull(y, nameof(y));

            var hashX = new HashSet<string>(x, StringComparer.OrdinalIgnoreCase);
            var hashY = new HashSet<string>(y, StringComparer.OrdinalIgnoreCase);
            if (hashX.Count <= hashY.Count)
            {
                return hashX.IsSubsetOf(hashY) ? (hashX.Count == hashY.Count ? 0 : -1) : int.MinValue;
            }

            return hashY.IsSubsetOf(hashX) ? 1 : int.MinValue;
        }

        public bool CompareComponent(IEnumerable<(string definition, string expression)> x, IEnumerable<(string definition, string expression)> y)
        {
            if (x == null || y == null)
            {
                return x == null && y == null;
            }

            var componetX = x.ToDictionary(x => x.definition, x => x.expression);
            var componetY = y.ToDictionary(x => x.definition, x => x.expression);
            if (componetX.Count != componetY.Count)
            {
                return false;
            }

            if (componetX.Any(x => !componetY.TryGetValue(x.Key, out var y) || CompareExpression(x.Value, y) != 0))
            {
                return false;
            }

            return true;
        }

        public int CompareExpression(string x, string y)
        {
            if (x == null || y == null)
            {
                return x == y ? 0 : (x == null ? -1 : 1);
            }

            try
            {
                var expX = _compiler.Parse(x);
                var expY = _compiler.Parse(y);
                return CompareExpression(expX, expY);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to compare expressions: '{x}', '{y}'");
                throw;
            }
        }

        private int CompareExpression(Expression x, Expression y)
        {
            if (x == null || y == null)
            {
                return x == y ? 0 : (x == null ? -1 : 1);
            }

            var expsX = new List<Expression>();
            var expsY = new List<Expression>();

            Flatten(x, expsX);
            Flatten(y, expsY);

            _logger.LogInformation($"Flatten '{x}' to {expsX.Count} expressions.");
            _logger.LogInformation($"Flatten '{y}' to {expsY.Count} expressions.");
            if (expsX.Count <= expsY.Count)
            {
                foreach (var expX in expsX)
                {
                    if (!expsY.Any(y => Equals(expX, y)))
                    {
                        return int.MinValue;
                    }
                }

                _logger.LogInformation($"{expsX.Count} expressions.");
                return expsX.Count == expsY.Count ? 0 : -1;
            }

            foreach (var expY in expsY)
            {
                if (!expsX.Any(x => Equals(expY, x)))
                {
                    return int.MinValue;
                }
            }

            return 1;
        }

        private static bool Equals(Expression x, Expression y)
        {
            if (x == null || y == null)
            {
                return x == y;
            }

            if (x is BracketExpression || y is BracketExpression)
            {
                x = x is BracketExpression ? ((BracketExpression)x).Operand : x;
                y = y is BracketExpression ? ((BracketExpression)y).Operand : y;
                return Equals(x, y);
            }

            var typeNameX = x.GetType().Name;
            var typeNameY = y.GetType().Name;
            if (!string.Equals(typeNameX, typeNameY, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            switch (typeNameX)
            {
                case nameof(AxisExpression):
                    var axisX = (AxisExpression)x;
                    var axisY = (AxisExpression)y;
                    return string.Equals(axisX.AxisName, axisY.AxisName, StringComparison.OrdinalIgnoreCase);

                case nameof(BinaryExpression):
                    var binX = (BinaryExpression)x;
                    var binY = (BinaryExpression)y;
                    if (!string.Equals(binX.Op, binY.Op, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    goto case nameof(FunctionCallExpression);

                case nameof(BracketExpression):
                    var bracketX = (BracketExpression)x;
                    var bracketY = (BracketExpression)y;
                    return Equals(bracketX.Operand, bracketY.Operand);

                case nameof(ChildExpression):
                    var childX = (ChildExpression)x;
                    var childY = (ChildExpression)y;
                    if (!string.Equals(childX.ChildName, childY.ChildName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    goto case nameof(FunctionCallExpression);

                case nameof(IdentifierExpression):
                case nameof(ConstantExpression):
                    var idX = (ConstantExpression)x;
                    var idY = (ConstantExpression)y;
                    if (!string.Equals(idX.Value?.ToString(), idY.Value?.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    return string.Equals(idX.Unit?.Value, idY.Unit?.Value, StringComparison.OrdinalIgnoreCase);

                case nameof(IndexerExpression):
                    var idxX = (IndexerExpression)x;
                    var idxY = (IndexerExpression)y;
                    if (!Equals(idxX.Index, idxY.Index))
                    {
                        return false;
                    }

                    goto case nameof(FunctionCallExpression);

                case nameof(NewNodeListInitExpression):
                    var newX = (NewNodeListInitExpression)x;
                    var newY = (NewNodeListInitExpression)y;
                    var contentsX = newX.Contents?.ToList() ?? new List<Expression>();
                    var contentsY = newY.Contents?.ToList() ?? new List<Expression>();
                    if (contentsX.Count != contentsY.Count)
                    {
                        return false;
                    }

                    for (int i = 0; i < contentsX.Count; i++)
                    {
                        if (!Equals(contentsX[i], contentsY[i]))
                        {
                            return false;
                        }
                    }

                    break;

                case nameof(UnaryExpression):
                    var unaryX = (UnaryExpression)x;
                    var unaryY = (UnaryExpression)y;
                    if (!string.Equals(unaryX.Op, unaryY.Op, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (!Equals(unaryX.Operand, unaryY.Operand))
                    {
                        return false;
                    }

                    goto case nameof(FunctionCallExpression);

                case nameof(VariableRefExpression):
                    var varX = (VariableRefExpression)x;
                    var varY = (VariableRefExpression)y;
                    return string.Equals(varX.Name, varY.Name, StringComparison.OrdinalIgnoreCase);

                case nameof(FunctionCallExpression):
                    var funcX = (FunctionCallExpression)x;
                    var funcY = (FunctionCallExpression)y;

                    if (!string.Equals(funcX.FunctionName, funcY.FunctionName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    if (!Equals(funcX.Focus, funcY.Focus))
                    {
                        return false;
                    }

                    var argsX = funcX.Arguments?.ToList() ?? new List<Expression>();
                    var argsY = funcY.Arguments?.ToList() ?? new List<Expression>();
                    if (argsX.Count != argsY.Count)
                    {
                        return false;
                    }

                    // TODO: does the ordering of arguments matter?
                    for (int i = 0; i < argsX.Count; i++)
                    {
                        if (!Equals(argsX[i], argsY[i]))
                        {
                            return false;
                        }
                    }

                    break;

                default:
                    return false;
            }

            return true;
        }

        private static void Flatten(Expression expression, List<Expression> expressions)
        {
            if (expression == null || expressions == null)
            {
                return;
            }

            switch (expression)
            {
                case BinaryExpression binaryExpression:
                    if (binaryExpression.Op == "|")
                    {
                        foreach (var arg in binaryExpression.Arguments)
                        {
                            Flatten(arg, expressions);
                        }
                    }
                    else
                    {
                        expressions.Add(expression);
                    }

                    break;

                case BracketExpression bracketExpression:
                    Flatten(bracketExpression.Operand, expressions);
                    break;

                default:
                    expressions.Add(expression);
                    break;
            }
        }
    }
}
