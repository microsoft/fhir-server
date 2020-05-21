// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using EnumerableReturnType = System.Collections.Generic.IEnumerable<Microsoft.Health.Fhir.Core.Features.Search.Parameters.SearchParameterTypeResult>;
using Expression = Hl7.FhirPath.Expressions.Expression;
using Range = Hl7.Fhir.Model.Range;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    /// <summary>
    /// Resolves the POCO types from a FHIR Path query
    /// </summary>
    [SuppressMessage("Design", "CA1801", Justification = "Visitor overloads are resolved dynamically so method signature should remain the same.")]
    internal static class SearchParameterToTypeResolver
    {
        private static readonly ModelInspector ModelInspector = GetModelInspector();

        internal static Action<string> Log { get; set; } = s => Debug.WriteLine(s);

        public static EnumerableReturnType Resolve(
            string resourceType,
            (SearchParamType type, Expression expression, Uri definition) typeAndExpression,
            (SearchParamType type, Expression expression, Uri definition)[] componentExpressions)
        {
            Type typeForFhirType = ModelInfoProvider.GetTypeForFhirType(resourceType);

            if (typeForFhirType == null)
            {
                // Not a FHIR Type
                yield break;
            }

            if (componentExpressions?.Any() == true)
            {
                foreach ((SearchParamType type, Expression expression, Uri definition) component in componentExpressions)
                {
                    var context = Context.WithParentType(typeForFhirType, component.type, component.definition, typeAndExpression.expression);

                    foreach (SearchParameterTypeResult classMapping in ClassMappings(context, component.expression))
                    {
                        yield return classMapping;
                    }
                }
            }
            else
            {
                var context = Context.WithParentType(typeForFhirType, typeAndExpression.type, typeAndExpression.definition);

                foreach (SearchParameterTypeResult classMapping in ClassMappings(context, typeAndExpression.expression))
                {
                    yield return classMapping;
                }
            }

            EnumerableReturnType ClassMappings(Context context, Expression expr)
            {
                foreach (SearchParameterTypeResult result in Accept(expr, context)
                    .GroupBy(x => x.ClassMapping.Name)
                    .Select(x => x.FirstOrDefault())
                    .ToArray())
                {
                    yield return result;
                }
            }
        }

        private static EnumerableReturnType Visit(ChildExpression expression, Context ctx)
        {
            if (expression.FunctionName == "builtin.children")
            {
                Context newCtx = ctx;
                if (expression.ChildName != null)
                {
                    newCtx = ctx.WithPath(expression.ChildName);
                }

                foreach (SearchParameterTypeResult type in Accept(expression.Focus, newCtx))
                {
                    yield return type;
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private static EnumerableReturnType Visit(BinaryExpression expression, Context ctx)
        {
            if (expression.Op == "as")
            {
                var constantExp = expression.Arguments.OfType<ConstantExpression>().Single().Value as string;
                ClassMapping mapping = GetMapping(constantExp);

                ctx = ctx.WithAsTypeMapping(mapping);

                foreach (SearchParameterTypeResult result in Accept(expression.Right, ctx.Clone()))
                {
                    yield return result;
                }
            }
            else if (expression.Op == "|" ||
                     expression.Op == "!=" ||
                     expression.Op == "==" ||
                     expression.Op == "and")
            {
                foreach (Expression innerExpression in expression.Arguments)
                {
                    foreach (SearchParameterTypeResult result in Accept(innerExpression, ctx.Clone()))
                    {
                        yield return result;
                    }
                }
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private static EnumerableReturnType Visit(FunctionCallExpression expression, Context ctx)
        {
            if (expression.Focus != null)
            {
                if (expression.FunctionName == "type")
                {
                    // Ignore
                    yield break;
                }

                if (expression.FunctionName == "exists" || expression.FunctionName == "is")
                {
                    yield return new SearchParameterTypeResult(GetMapping(typeof(FhirBoolean)), ctx.SearchParamType, null, ctx.Definition);

                    yield break;
                }

                if (expression.FunctionName == "as")
                {
                    // Matches Condition.abatement.as(Age)
                    var constantExp = expression.Arguments.OfType<ConstantExpression>().Single().Value as string;

                    ClassMapping mapping = GetMapping(constantExp);
                    ctx = ctx.WithAsTypeMapping(mapping);

                    foreach (SearchParameterTypeResult type in Accept(expression.Focus, ctx))
                    {
                        yield return type;
                    }

                    yield break;
                }

                if (expression.FunctionName == "where" ||
                    expression.FunctionName == "builtin.children" ||
                    expression.FunctionName == "builtin.item")
                {
                    foreach (SearchParameterTypeResult type in Accept(expression.Focus, ctx))
                    {
                        yield return type;
                    }

                    yield break;
                }
            }

            throw new NotImplementedException();
        }

        private static EnumerableReturnType Visit(AxisExpression expression, Context ctx)
        {
            if (ctx.ParentExpression != null)
            {
                foreach (SearchParameterTypeResult result in Accept(ctx.ParentExpression, ctx.CloneAsChildExpression()))
                {
                    yield return result;
                }
            }
            else
            {
                var pathBuilder = new StringBuilder(ctx.Path.First().Item1);

                var skipResourceElement = true;
                ClassMapping mapping = ctx.Path.First().Item2;

                if (mapping == null && ModelInfoProvider.Instance.GetTypeForFhirType(ctx.Path.First().Item1) != null)
                {
                    mapping = GetMapping(ctx.Path.First().Item1);
                }

                // Default to parent resource
                if (mapping == null)
                {
                    mapping = ctx.ParentTypeMapping;
                    skipResourceElement = false;
                }

                foreach ((string, ClassMapping) item in ctx.Path.Skip(skipResourceElement ? 1 : 0))
                {
                    pathBuilder.AppendFormat(".{0}", item.Item1);
                    if (item.Item2 != null)
                    {
                        pathBuilder.AppendFormat("({0})", item.Item2.Name);
                        mapping = item.Item2;

                        continue;
                    }

                    PropertyMapping prop = mapping.PropertyMappings.FirstOrDefault(x => x.Name == item.Item1);
                    if (prop != null)
                    {
                        if (prop.GetElementType() == typeof(Element))
                        {
                            string path = pathBuilder.ToString();
                            foreach (Type fhirType in prop.FhirType)
                            {
                                yield return new SearchParameterTypeResult(GetMapping(fhirType), ctx.SearchParamType, path, ctx.Definition);
                            }

                            pathBuilder.AppendFormat("({0})", string.Join(",", prop.FhirType.Select(x => x.Name)));
                            Log($"Resolved path '{pathBuilder}'");

                            yield break;
                        }

                        mapping = GetMapping(prop.GetElementType());
                    }
                    else
                    {
                        break;
                    }
                }

                Log($"Resolved path '{pathBuilder}'");

                yield return new SearchParameterTypeResult(mapping, ctx.SearchParamType, pathBuilder.ToString(), ctx.Definition);
            }
        }

        private static EnumerableReturnType Visit(ConstantExpression expression, Context ctx)
        {
            yield break;
        }

        private static EnumerableReturnType Visit(VariableRefExpression expression, Context ctx)
        {
            // matches %resource.referenceSeq.chromosome
            if (string.Equals(expression.Name, "resource", StringComparison.OrdinalIgnoreCase))
            {
                Context newContext = ctx.WithPath("Resource", ctx.ParentTypeMapping);

                return Accept(new AxisExpression("that"), newContext);
            }

            throw new NotImplementedException();
        }

        private static EnumerableReturnType Visit(Expression expression, Context ctx)
        {
            throw new NotImplementedException();
        }

        private static EnumerableReturnType Accept(Expression expression, Context ctx)
        {
            return (EnumerableReturnType)Visit((dynamic)expression, ctx);
        }

        private static ClassMapping GetMapping(string type)
        {
            switch (type.ToUpperInvariant())
            {
                case "AGE":
                    return GetMapping(typeof(Age));
                case "DATETIME":
                case "DATE":
                    return GetMapping(typeof(FhirDateTime));
                case "URI":
                    return GetMapping(typeof(FhirUri));
                case "BOOLEAN":
                    return GetMapping(typeof(FhirBoolean));
                case "STRING":
                    return GetMapping(typeof(FhirString));
                case "PERIOD":
                    return GetMapping(typeof(Period));
                case "RANGE":
                    return GetMapping(typeof(Range));
                default:
                    return GetMapping(ModelInfoProvider.Instance.GetTypeForFhirType(type));
            }
        }

        private static ClassMapping GetMapping(Type type)
        {
            ClassMapping returnValue = ModelInspector.FindClassMappingByType(type);

            if (returnValue == null)
            {
                return ModelInspector.ImportType(type);
            }

            return returnValue;
        }

        private static ModelInspector GetModelInspector()
        {
#if Stu3
            // This method was internal in STU3
            PropertyInfo inspector = typeof(BaseFhirParser).GetProperty("Inspector", BindingFlags.Static | BindingFlags.NonPublic);

            if (inspector != null)
            {
                return (ModelInspector)inspector.GetValue(null);
            }

            throw new MissingMemberException($"{nameof(BaseFhirParser)}.Inspector property was not able to be accessed.");
#else
            return BaseFhirParser.Inspector;
#endif
        }

        private class Context
        {
            public Stack<(string, ClassMapping)> Path { get; set; } = new Stack<(string, ClassMapping)>();

            public ClassMapping ParentTypeMapping { get; set; }

            public Expression ParentExpression { get; set; }

            public ClassMapping AsTypeMapping { get; set; }

            public Uri Definition { get; set; }

            public SearchParamType SearchParamType { get; set; }

            public Context WithAsTypeMapping(ClassMapping asTypeMapping)
            {
                Context ctx = Clone();
                ctx.AsTypeMapping = asTypeMapping;

                return ctx;
            }

            public Context WithPath(string propertyName, ClassMapping knownMapping = null)
            {
                Context ctx = Clone();
                ctx.Path.Push((propertyName, knownMapping ?? AsTypeMapping));

                return ctx;
            }

            public static Context WithParentType(
                Type type,
                SearchParamType paramType,
                Uri definition,
                Expression parentExpression = null)
            {
                var ctx = new Context
                {
                    ParentExpression = parentExpression,
                    ParentTypeMapping = GetMapping(type),
                    SearchParamType = paramType,
                    Definition = definition,
                };

                return ctx;
            }

            public Context Clone()
            {
                var clone = new Stack<(string, ClassMapping)>();
                foreach ((string, ClassMapping) item in Path.Reverse())
                {
                    clone.Push(item);
                }

                var ctx = new Context
                {
                    Path = clone,
                    AsTypeMapping = AsTypeMapping,
                    ParentExpression = ParentExpression,
                    SearchParamType = SearchParamType,
                    ParentTypeMapping = ParentTypeMapping,
                    Definition = Definition,
                };

                return ctx;
            }

            public Context CloneAsChildExpression()
            {
                Context ctx = Clone();
                ctx.ParentExpression = null;

                return ctx;
            }
        }
    }
}
