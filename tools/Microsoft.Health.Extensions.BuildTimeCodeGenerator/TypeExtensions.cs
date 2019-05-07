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
        /// <summary>
        /// Converts a <see cref="Type"/> to a <see cref="TypeSyntax"/>.
        /// </summary>
        /// <param name="t">The type</param>
        /// <param name="useGlobalAlias">Whether the to qualify type names with "global::"</param>
        /// <returns>A <see cref="TypeSyntax"/> representing the type.</returns>
        public static TypeSyntax ToTypeSyntax(this Type t, bool useGlobalAlias = false)
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
                    t.GetElementType().ToTypeSyntax(useGlobalAlias),
                    SyntaxFactory.SingletonList(
                        SyntaxFactory.ArrayRankSpecifier(
                            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                SyntaxFactory.OmittedArraySizeExpression()))));
            }

            TypeSyntax qualification = t.IsNested
                ? t.DeclaringType.ToTypeSyntax(useGlobalAlias)
                : t.Namespace.Split('.')
                    .Select(s => (NameSyntax)SyntaxFactory.IdentifierName(s))
                    .Aggregate((acc, next) =>
                    {
                        // see if we should qualify with global::
                        NameSyntax left = useGlobalAlias && acc is IdentifierNameSyntax identifier
                            ? SyntaxFactory.AliasQualifiedName(SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxKind.GlobalKeyword)), identifier)
                            : acc;

                        return SyntaxFactory.QualifiedName(left, (SimpleNameSyntax)next);
                    });

            SimpleNameSyntax name = t.IsGenericType
                ? SyntaxFactory.GenericName(t.Name.Substring(0, t.Name.IndexOf('`', StringComparison.Ordinal)))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(t.GetGenericArguments().Select(typeArg => typeArg.ToTypeSyntax(useGlobalAlias)))))
                : (SimpleNameSyntax)SyntaxFactory.IdentifierName(t.Name);

            return SyntaxFactory.QualifiedName((NameSyntax)qualification, name);
        }
    }
}
