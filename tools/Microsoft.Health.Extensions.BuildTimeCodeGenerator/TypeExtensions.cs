// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
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

            if (t.IsPointer)
            {
                return SyntaxFactory.PointerType(t.GetElementType().ToTypeSyntax(useGlobalAlias));
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
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(t.GenericTypeArguments.Select(typeArg => typeArg.ToTypeSyntax(useGlobalAlias)))))
                : (SimpleNameSyntax)SyntaxFactory.IdentifierName(t.Name);

            return SyntaxFactory.QualifiedName((NameSyntax)qualification, name);
        }

        /// <summary>
        /// Creates a closed generic type from the a generic type definition (like <see cref="IEnumerable{T}"/>) and type arguments (like <see cref="string"/>).
        /// </summary>
        /// <param name="genericTypeDefinition">The generic type definition</param>
        /// <param name="typeArguments">Type arguments</param>
        /// <returns>The generic type.</returns>
        public static TypeSyntax CreateGenericTypeFromGenericTypeDefinition(TypeSyntax genericTypeDefinition, params TypeSyntax[] typeArguments)
        {
            GenericNameSyntax openType = genericTypeDefinition.DescendantNodes().OfType<GenericNameSyntax>().Single();
            GenericNameSyntax closedType = openType.AddTypeArgumentListArguments(typeArguments);

            return genericTypeDefinition.ReplaceNode(openType, closedType);
        }
    }
}
