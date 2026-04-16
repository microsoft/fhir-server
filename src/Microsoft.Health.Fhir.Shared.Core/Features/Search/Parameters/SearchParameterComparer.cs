// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Expression = Hl7.FhirPath.Expressions.Expression;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public class SearchParameterComparer : ISearchParameterComparer<SearchParameterInfo>
    {
        private readonly FhirPathCompiler _compiler;
        private readonly ILogger<ISearchParameterComparer<SearchParameterInfo>> _logger;

        public SearchParameterComparer(ILogger<ISearchParameterComparer<SearchParameterInfo>> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _compiler = new FhirPathCompiler();
            _logger = logger;
        }

        public int Compare(SearchParameterInfo x, SearchParameterInfo y)
        {
            EnsureArg.IsNotNull(x, nameof(x));
            EnsureArg.IsNotNull(y, nameof(y));

            // TODO: need 'derivedFrom' to compare the url properly. (https://hl7.org/fhir/searchparameter-definitions.html#SearchParameter.derivedFrom)
            var isBaseTypeSearchParameter = x.IsBaseTypeSearchParameter() || y.IsBaseTypeSearchParameter();
            var result = 0;
            if (!isBaseTypeSearchParameter)
            {
                if (!string.Equals(x.Url.OriginalString, y.Url.OriginalString, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Url mismatch: '{x.Url}', '{x.Url}'");
                    return int.MinValue;
                }

                var baseX = x.BaseResourceTypes?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
                var baseY = y.BaseResourceTypes?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
                result = CompareBase(baseX, baseY);
                if (result == int.MinValue)
                {
                    _logger.LogInformation($"Base mismatch: '{string.Join(",", baseX)}', '{string.Join(",", baseY)}'");
                    return result;
                }
            }

            if (!string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Code mismatch: '{x.Code}', '{x.Code}'");
                return int.MinValue;
            }

            if (x.Type != y.Type)
            {
                _logger.LogInformation($"Type mismatch: '{x.Type}', '{x.Type}'");
                return int.MinValue;
            }

            var expressionResult = CompareExpression(x.Expression, y.Expression, isBaseTypeSearchParameter);
            if (expressionResult == int.MinValue)
            {
                _logger.LogInformation($"Expression mismatch: '{x.Expression}', '{y.Expression}'");
                return expressionResult;
            }

            result = result == 0 ? expressionResult : result;
            if (x.Type == ValueSets.SearchParamType.Composite)
            {
                var componentX = x.Component.Select<SearchParameterComponentInfo, (string, string)>(x => new(x.DefinitionUrl.OriginalString, x.Expression)).ToList();
                var componentY = y.Component.Select<SearchParameterComponentInfo, (string, string)>(x => new(x.DefinitionUrl.OriginalString, x.Expression)).ToList();
                var componentResult = CompareComponent(componentX, componentY);
                if (componentResult == int.MinValue)
                {
                    _logger.LogInformation($"Component mismatch: '{componentX.Count} components', '{componentY.Count} components'");
                    return componentResult;
                }

                if ((result != 0 && componentResult != 0 && result != componentResult) || (expressionResult != 0 && componentResult != 0 && expressionResult != componentResult))
                {
                    _logger.LogInformation($"Superset/subset relation mismatch: base={result}, component={componentResult}");
                    return int.MinValue;
                }

                result = result == 0 ? componentResult : result;
            }

            return result;
        }

        public int CompareBase(IEnumerable<string> x, IEnumerable<string> y)
        {
            EnsureArg.IsNotNull(x, nameof(x));
            EnsureArg.IsNotNull(y, nameof(y));

            var basesX = x.ToList();
            var basesY = y.ToList();
            if (basesX.Count <= basesY.Count)
            {
                if (!basesX.All(x => basesY.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase))))
                {
                    return int.MinValue;
                }

                return basesX.Count == basesY.Count ? 0 : -1;
            }

            if (!basesY.All(y => basesX.Any(x => string.Equals(x, y, StringComparison.OrdinalIgnoreCase))))
            {
                return int.MinValue;
            }

            return basesX.Count == basesY.Count ? 0 : 1;
        }

        public int CompareComponent(IEnumerable<(string definition, string expression)> x, IEnumerable<(string definition, string expression)> y)
        {
            EnsureArg.IsNotNull(x, nameof(x));
            EnsureArg.IsNotNull(y, nameof(y));

            var compsX = x.ToList();
            var compsY = y.ToList();
            if (compsX.Count <= compsY.Count)
            {
                if (!compsX.All(x => compsY.Any(y => string.Equals(x.definition, y.definition, StringComparison.OrdinalIgnoreCase) && CompareExpression(x.expression, y.expression) == 0)))
                {
                    return int.MinValue;
                }

                return compsX.Count == compsY.Count ? 0 : -1;
            }

            if (!compsY.All(y => compsX.Any(x => string.Equals(x.definition, y.definition, StringComparison.OrdinalIgnoreCase) && CompareExpression(x.expression, y.expression) == 0)))
            {
                return int.MinValue;
            }

            return compsX.Count == compsY.Count ? 0 : 1;
        }

        public int CompareExpression(string x, string y, bool baseTypeExpression = false)
        {
            if (x == null || y == null)
            {
                return int.MinValue;
            }

            try
            {
                if (baseTypeExpression)
                {
                    return CompareBaseTypeExpression(x, y);
                }

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

        private static int CompareBaseTypeExpression(string x, string y)
        {
            if (x == null || y == null)
            {
                return int.MinValue;
            }

            var resourceTypeX = x.Substring(0, x.IndexOf('.', StringComparison.Ordinal));
            var resourceTypeY = y.Substring(0, y.IndexOf('.', StringComparison.Ordinal));
            var expX = x.Substring(x.IndexOf('.', StringComparison.Ordinal));
            var expY = y.Substring(y.IndexOf('.', StringComparison.Ordinal));
            if (!string.Equals(expX, expY, StringComparison.OrdinalIgnoreCase))
            {
                return int.MinValue;
            }

            if (string.Equals(resourceTypeX, resourceTypeY, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (IsBaseResourceType(resourceTypeX))
            {
                return -1;
            }

            return IsBaseResourceType(resourceTypeY) ? 1 : int.MinValue;
        }

        private int CompareExpression(Expression x, Expression y)
        {
            if (x == null || y == null)
            {
                return int.MinValue;
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
                return x == null && y == null;
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

        private static bool IsBaseResourceType(string resourceType)
        {
            return string.Equals(resourceType, KnownResourceTypes.Resource, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, KnownResourceTypes.DomainResource, StringComparison.OrdinalIgnoreCase);
        }
    }
}
