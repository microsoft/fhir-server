// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator
{
    internal static class TypeExtensions
    {
        public static TypeSyntax ToTypeSyntax(this Type t)
        {
            if (t == typeof(void))
            {
                return SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword));
            }

            if (t.IsGenericParameter)
            {
                return SyntaxFactory.IdentifierName(t.Name);
            }

            if (t.IsArray)
            {
                return SyntaxFactory.ArrayType(
                    t.GetElementType().ToTypeSyntax(),
                    SyntaxFactory.SingletonList(
                        SyntaxFactory.ArrayRankSpecifier(
                            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                SyntaxFactory.OmittedArraySizeExpression()))));
            }

            TypeSyntax qualification = t.IsNested
                ? t.DeclaringType.ToTypeSyntax()
                : t.Namespace.Split('.').Select(s => (NameSyntax)SyntaxFactory.IdentifierName(s)).Aggregate((acc, next) => SyntaxFactory.QualifiedName(acc, (SimpleNameSyntax)next));

            SimpleNameSyntax name = t.IsGenericType
                ? SyntaxFactory.GenericName(t.Name.Substring(0, t.Name.IndexOf('`', StringComparison.Ordinal)))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(t.GetGenericArguments().Select(ToTypeSyntax))))
                : (SimpleNameSyntax)SyntaxFactory.IdentifierName(t.Name);

            return SyntaxFactory.QualifiedName((NameSyntax)qualification, name);
        }
    }
}
