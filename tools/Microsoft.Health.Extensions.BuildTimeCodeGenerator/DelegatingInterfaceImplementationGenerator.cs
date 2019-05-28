// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator
{
    /// <summary>
    /// Generates a class that implements one or more interfaces, delegating implementation to an inner field.
    /// Implementations are explicit.
    /// </summary>
    internal class DelegatingInterfaceImplementationGenerator : ICodeGenerator
    {
        internal const string DeclaringTypeKind = "DeclaringType";
        private readonly SyntaxTokenList _typeModifiers;
        private readonly SyntaxTokenList _constructorModifiers;
        private readonly Type[] _interfacesToImplement;
        private static readonly IdentifierNameSyntax FieldName = IdentifierName("_inner");
        private static readonly AttributeListSyntax ExcludeFromCodeCoverageAttributeSyntax = AttributeList(SingletonSeparatedList(Attribute(IdentifierName(typeof(ExcludeFromCodeCoverageAttribute).FullName))));

        public DelegatingInterfaceImplementationGenerator(SyntaxTokenList typeModifiers, SyntaxTokenList constructorModifiers, params Type[] interfacesToImplement)
        {
            _typeModifiers = typeModifiers;
            _constructorModifiers = constructorModifiers;
            _interfacesToImplement = interfacesToImplement;
        }

        public (MemberDeclarationSyntax, UsingDirectiveSyntax[]) Generate(string typeName)
        {
            var classDeclarration = ClassDeclaration(typeName)
                .WithModifiers(_typeModifiers)
                .WithBaseList(BaseList(SeparatedList(_interfacesToImplement.Select(t => (BaseTypeSyntax)SimpleBaseType(t.ToTypeSyntax())))))
                .AddMembers(
                    FieldDeclaration(VariableDeclaration(_interfacesToImplement[0].ToTypeSyntax()).AddVariables(VariableDeclarator(FieldName.Identifier))).AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword)),
                    GetConstructor(typeName))
                .AddMembers(GetPropertiesAndMethods().ToArray());
            return (classDeclarration, new UsingDirectiveSyntax[0]);
        }

        private ConstructorDeclarationSyntax GetConstructor(string className)
        {
            return ConstructorDeclaration(className)
                .WithModifiers(_constructorModifiers)
                .AddParameterListParameters(Parameter(Identifier("inner")).WithType(_interfacesToImplement[0].ToTypeSyntax()))
                .AddBodyStatements(
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            FieldName,
                            BinaryExpression(
                                SyntaxKind.CoalesceExpression,
                                IdentifierName("inner"),
                                ThrowExpression(
                                    ObjectCreationExpression(typeof(ArgumentNullException).ToTypeSyntax())
                                        .AddArgumentListArguments(Argument(
                                            InvocationExpression(
                                                    IdentifierName("nameof"))
                                                .AddArgumentListArguments(Argument(
                                                    IdentifierName("inner"))))))))));
        }

        private IEnumerable<MemberDeclarationSyntax> GetPropertiesAndMethods()
        {
            for (var interfaceIndex = 0; interfaceIndex < _interfacesToImplement.Length; interfaceIndex++)
            {
                var interfaceType = _interfacesToImplement[interfaceIndex];

                var typedFieldName = interfaceIndex == 0 ? (ExpressionSyntax)FieldName : ParenthesizedExpression(CastExpression(interfaceType.ToTypeSyntax(), FieldName));
                var explicitInterfaceSpecifier = ExplicitInterfaceSpecifier((NameSyntax)interfaceType.ToTypeSyntax());

                foreach (var propertyInfo in interfaceType.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    ParameterInfo[] indexParameters = propertyInfo.GetIndexParameters();
                    MethodInfo getter = propertyInfo.GetGetMethod();
                    MethodInfo setter = propertyInfo.GetSetMethod();

                    BasePropertyDeclarationSyntax propertyDeclarationSyntax = (indexParameters.Length == 0
                            ? (BasePropertyDeclarationSyntax)PropertyDeclaration(propertyInfo.PropertyType.ToTypeSyntax(), propertyInfo.Name)
                            : IndexerDeclaration(propertyInfo.PropertyType.ToTypeSyntax())
                                .AddParameterListParameters(indexParameters.Select(p => Parameter(Identifier(p.Name)).WithType(p.ParameterType.ToTypeSyntax())).ToArray()))
                        .WithExplicitInterfaceSpecifier(explicitInterfaceSpecifier)
                        .AddAttributeLists(ExcludeFromCodeCoverageAttributeSyntax)
                        .WithAdditionalAnnotations(new SyntaxAnnotation(DeclaringTypeKind, (getter?.GetBaseDefinition() ?? setter?.GetBaseDefinition())?.DeclaringType.FullName));

                    if (getter != null)
                    {
                        propertyDeclarationSyntax = propertyDeclarationSyntax.AddAccessorListAccessors(
                            AccessorDeclaration(
                                SyntaxKind.GetAccessorDeclaration,
                                Block(ReturnStatement(
                                    indexParameters.Length == 0
                                        ? (ExpressionSyntax)MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, FieldName, IdentifierName(propertyInfo.Name))
                                        : ElementAccessExpression(FieldName).AddArgumentListArguments(indexParameters.Select(p => Argument(IdentifierName(p.Name))).ToArray())))));
                    }

                    if (setter != null)
                    {
                        ExpressionSyntax assignmentTarget = indexParameters.Length == 0
                            ? (ExpressionSyntax)MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, typedFieldName, IdentifierName(propertyInfo.Name))
                            : ElementAccessExpression(FieldName).AddArgumentListArguments(indexParameters.Select(p => Argument(IdentifierName(p.Name))).ToArray());

                        propertyDeclarationSyntax = propertyDeclarationSyntax.AddAccessorListAccessors(
                            AccessorDeclaration(
                                SyntaxKind.SetAccessorDeclaration,
                                Block(ExpressionStatement(AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    assignmentTarget,
                                    IdentifierName("value"))))));
                    }

                    yield return propertyDeclarationSyntax;
                }

                IOrderedEnumerable<MethodInfo> orderedMethods = interfaceType
                    .GetMethods()
                    .Except(interfaceType.GetProperties().SelectMany(p => p.GetAccessors()))
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ThenBy(m => string.Join(", ", m.GetParameters().Select(p => p.ParameterType.FullName)), StringComparer.Ordinal);

                foreach (var methodInfo in orderedMethods)
                {
                    var method = MethodDeclaration(methodInfo.ReturnType.ToTypeSyntax(), methodInfo.Name)
                        .WithExplicitInterfaceSpecifier(explicitInterfaceSpecifier)
                        .AddParameterListParameters(
                            methodInfo.GetParameters().Select(p =>
                                    Parameter(Identifier(p.Name))
                                        .WithType(p.ParameterType.ToTypeSyntax())
                                        .WithModifiers(p.IsDefined(typeof(ParamArrayAttribute), false) ? TokenList(Token(SyntaxKind.ParamsKeyword)) : TokenList()))
                                .ToArray())
                        .AddAttributeLists(ExcludeFromCodeCoverageAttributeSyntax)
                        .WithBody(Block())
                        .WithAdditionalAnnotations(new SyntaxAnnotation(DeclaringTypeKind, methodInfo.GetBaseDefinition().DeclaringType.FullName));

                    if (methodInfo.IsGenericMethod)
                    {
                        method = method.WithTypeParameterList(TypeParameterList(SeparatedList(methodInfo.GetGenericArguments().Select(t => TypeParameter(t.Name)))));
                    }

                    var methodName = methodInfo.IsGenericMethod
                        ? GenericName(methodInfo.Name).AddTypeArgumentListArguments(methodInfo.GetGenericArguments().Select(t => t.ToTypeSyntax()).ToArray())
                        : (SimpleNameSyntax)IdentifierName(methodInfo.Name);

                    var invocation = InvocationExpression(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            typedFieldName,
                            methodName),
                        ArgumentList(SeparatedList(methodInfo.GetParameters().Select(p => Argument(IdentifierName(p.Name))))));

                    var block = Block(methodInfo.ReturnType == typeof(void) ? ExpressionStatement(invocation) : (StatementSyntax)ReturnStatement(invocation));

                    method = method.WithBody(block);

                    yield return method;
                }
            }
        }
    }
}
