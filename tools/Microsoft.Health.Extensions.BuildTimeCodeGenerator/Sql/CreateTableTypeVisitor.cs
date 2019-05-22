// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.Sql
{
    /// <summary>
    /// Visits a SQL AST, creating a class and a struct for each CREATE TABLE TYPE statement.
    /// The class derives from TableValuedParameterDefinition and the struct represents a row
    /// of data, with a constructor signature and fields that match the columns of the table type.
    /// </summary>
    public class CreateTableTypeVisitor : SqlVisitor
    {
        public override int ArtifactSortOder => 2;

        public override void Visit(CreateTypeTableStatement node)
        {
            string tableTypeName = node.Name.BaseIdentifier.Value;
            string schemaQualifiedTableTypeName = $"{node.Name.SchemaIdentifier.Value}.{tableTypeName}";
            string className = GetClassNameForTableValuedParameterDefinition(node.Name);
            string rowStructName = GetRowStructNameForTableType(node.Name);

            TypeSyntax columnsEnumerableType = TypeExtensions.CreateGenericTypeFromGenericTypeDefinition(
                typeof(IEnumerable<>).ToTypeSyntax(true),
                IdentifierName("Column"));

            ArrayTypeSyntax columnsArrayType = ArrayType(IdentifierName("Column")).AddRankSpecifiers(ArrayRankSpecifier());

            ClassDeclarationSyntax classDeclarationSyntax =
                ClassDeclaration(className)
                    .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword)))
                    .AddBaseListTypes(
                        SimpleBaseType(
                            GenericName("TableValuedParameterDefinition")
                                .AddTypeArgumentListArguments(IdentifierName(rowStructName))))
                    .AddMembers(
                        ConstructorDeclaration(
                                Identifier(className))
                            .WithModifiers(
                                TokenList(
                                    Token(SyntaxKind.InternalKeyword)))
                            .AddParameterListParameters(
                                Parameter(Identifier("parameterName")).WithType(typeof(string).ToTypeSyntax(true)))
                            .WithInitializer(
                                ConstructorInitializer(
                                    SyntaxKind.BaseConstructorInitializer,
                                    ArgumentList(SeparatedList(new[]
                                    {
                                        Argument(IdentifierName("parameterName")),
                                        Argument(
                                            LiteralExpression(
                                                SyntaxKind.StringLiteralExpression,
                                                Literal(schemaQualifiedTableTypeName))),
                                    }))))
                            .WithBody(Block()))
                    .AddMembers(node.Definition.ColumnDefinitions.Select(CreatePropertyForColumn).ToArray())

                    // Add Columns property override
                    .AddMembers(
                        PropertyDeclaration(
                                columnsEnumerableType,
                                Identifier("Columns"))
                            .AddModifiers(Token(SyntaxKind.ProtectedKeyword), Token(SyntaxKind.OverrideKeyword))
                            .WithExpressionBody(
                                ArrowExpressionClause(
                                    ArrayCreationExpression(columnsArrayType)
                                        .WithInitializer(
                                            InitializerExpression(
                                                SyntaxKind.ArrayInitializerExpression,
                                                SeparatedList<ExpressionSyntax>(
                                                    node.Definition.ColumnDefinitions.Select(c => IdentifierName(c.ColumnIdentifier.Value)))))))
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))

                    // Add FillSqlDataRecord implementation
                    .AddMembers(
                        MethodDeclaration(typeof(void).ToTypeSyntax(), Identifier("FillSqlDataRecord"))
                            .AddModifiers(Token(SyntaxKind.ProtectedKeyword), Token(SyntaxKind.OverrideKeyword))
                            .AddParameterListParameters(
                                Parameter(Identifier("record")).WithType(typeof(SqlDataRecord).ToTypeSyntax(useGlobalAlias: true)),
                                Parameter(
                                    Identifier("rowData")).WithType(IdentifierName(rowStructName)))
                            .WithBody(
                                Block(node.Definition.ColumnDefinitions.Select((c, i) =>
                                    ExpressionStatement(
                                        InvocationExpression(
                                                MemberAccessExpression(
                                                    SyntaxKind.SimpleMemberAccessExpression,
                                                    IdentifierName(c.ColumnIdentifier.Value),
                                                    IdentifierName("Set")))
                                            .AddArgumentListArguments(
                                                Argument(IdentifierName("record")),
                                                Argument(
                                                    LiteralExpression(
                                                        SyntaxKind.NumericLiteralExpression,
                                                        Literal(i))),
                                                Argument(
                                                    MemberAccessExpression(
                                                        SyntaxKind.SimpleMemberAccessExpression,
                                                        IdentifierName("rowData"),
                                                        IdentifierName(c.ColumnIdentifier.Value)))))))));

            StructDeclarationSyntax rowStruct = StructDeclaration(rowStructName)
                .AddModifiers(Token(SyntaxKind.InternalKeyword))

                // Add a constructor with parameters for each column, setting the associate property for each column.
                .AddMembers(
                    ConstructorDeclaration(
                            Identifier(rowStructName))
                        .WithModifiers(
                            TokenList(
                                Token(SyntaxKind.InternalKeyword)))
                        .AddParameterListParameters(
                            node.Definition.ColumnDefinitions.Select(c =>
                                Parameter(Identifier(c.ColumnIdentifier.Value))
                                    .WithType(DataTypeReferenceToClrType(c.DataType, IsColumnNullable(c)))).ToArray())
                        .WithBody(
                            Block(node.Definition.ColumnDefinitions.Select(c =>
                                ExpressionStatement(
                                    AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        left: MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            ThisExpression(),
                                            IdentifierName(c.ColumnIdentifier.Value)),
                                        right: IdentifierName(c.ColumnIdentifier.Value)))))))

                // Add a property for each column
                .AddMembers(node.Definition.ColumnDefinitions.Select(c =>
                    (MemberDeclarationSyntax)PropertyDeclaration(
                            DataTypeReferenceToClrType(c.DataType, IsColumnNullable(c)),
                            Identifier(c.ColumnIdentifier.Value))
                        .AddModifiers(Token(SyntaxKind.InternalKeyword))
                        .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))).ToArray());

            MembersToAdd.Add(classDeclarationSyntax.AddSortingKey(this, tableTypeName));
            MembersToAdd.Add(rowStruct.AddSortingKey(this, tableTypeName));

            base.Visit(node);
        }
    }
}
