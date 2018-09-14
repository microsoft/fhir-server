// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator
{
    internal class DelegatingInterfaceImplementationGenerator : ICodeGenerator
    {
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

        public SyntaxNode Generate(string namespaceName, string typeName, Compilation compilation)
        {
            return CompilationUnit()
                .AddMembers(
                    NamespaceDeclaration(IdentifierName(namespaceName))
                        .AddMembers(GetClass(typeName)));
        }

        public ClassDeclarationSyntax GetClass(string typeName)
        {
            return ClassDeclaration(typeName)
                .WithModifiers(_typeModifiers)
                .WithBaseList(BaseList(SeparatedList(_interfacesToImplement.Select(t => (BaseTypeSyntax)SimpleBaseType(t.ToTypeSyntax())))))
                .AddMembers(
                    FieldDeclaration(VariableDeclaration(_interfacesToImplement[0].ToTypeSyntax()).AddVariables(VariableDeclarator(FieldName.Identifier))).AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword)),
                    GetConstructor(typeName))
                .AddMembers(GetPropertiesAndMethods().ToArray());
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

                foreach (var propertyInfo in interfaceType.GetProperties())
                {
                    var propertyDeclarationSyntax = PropertyDeclaration(propertyInfo.PropertyType.ToTypeSyntax(), propertyInfo.Name)
                        .WithExplicitInterfaceSpecifier(explicitInterfaceSpecifier)
                        .AddAttributeLists(ExcludeFromCodeCoverageAttributeSyntax);

                    if (propertyInfo.GetGetMethod() != null)
                    {
                        propertyDeclarationSyntax = propertyDeclarationSyntax.AddAccessorListAccessors(
                            AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, Block(ReturnStatement(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, FieldName, IdentifierName(propertyInfo.Name))))));
                    }

                    if (propertyInfo.GetSetMethod() != null)
                    {
                        propertyDeclarationSyntax = propertyDeclarationSyntax.AddAccessorListAccessors(
                            AccessorDeclaration(
                                SyntaxKind.SetAccessorDeclaration,
                                Block(ExpressionStatement(AssignmentExpression(
                                    SyntaxKind.SimpleAssignmentExpression,
                                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, typedFieldName, IdentifierName(propertyInfo.Name)),
                                    IdentifierName("value"))))));
                    }

                    yield return propertyDeclarationSyntax;
                }

                foreach (var methodInfo in interfaceType.GetMethods().Except(interfaceType.GetProperties().SelectMany(p => p.GetAccessors())))
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
                        .WithBody(Block());

                    if (methodInfo.IsGenericMethod)
                    {
                        method = method.WithTypeParameterList(TypeParameterList(SeparatedList(methodInfo.GetGenericArguments().Select(t => TypeParameter(t.Name)))));
                    }

                    var methodName = methodInfo.IsGenericMethod
                        ? GenericName(methodInfo.Name).AddTypeArgumentListArguments(methodInfo.GetGenericArguments().Select(TypeExtensions.ToTypeSyntax).ToArray())
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
