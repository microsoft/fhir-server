// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.Server;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.Sql
{
    public class CreateTableTypeVisitor : SqlVisitor
    {
        public override void Visit(CreateTypeTableStatement node)
        {
            string tableTypeName = node.Name.BaseIdentifier.Value;
            string schemaQualifiedTableTypeName = $"{node.Name.SchemaIdentifier.Value}.{tableTypeName}";
            string className = $"{tableTypeName}TableValuedParameterDefinition";
            string rowStructName = $"{tableTypeName}Row";

            ArrayTypeSyntax columnsArrayType = ArrayType(IdentifierName("Column")).AddRankSpecifiers(ArrayRankSpecifier());

            ClassDeclarationSyntax classDeclarationSyntax =
                ClassDeclaration(className)
                    .WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))
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
                            .WithInitializer(
                                ConstructorInitializer(
                                    SyntaxKind.BaseConstructorInitializer,
                                    ArgumentList(
                                        SingletonSeparatedList(
                                            Argument(
                                                LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    Literal(schemaQualifiedTableTypeName)))))))
                            .WithBody(Block()))
                    .AddMembers(node.Definition.ColumnDefinitions.Select(CreatePropertyForColumn).ToArray())

                    // Add Columns property override
                    .AddMembers(
                        PropertyDeclaration(
                                columnsArrayType,
                                Identifier("Columns"))
                            .AddModifiers(Token(SyntaxKind.ProtectedKeyword), Token(SyntaxKind.OverrideKeyword))
                            .WithExpressionBody(
                                ArrowExpressionClause(
                                    ArrayCreationExpression(
                                            columnsArrayType)
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
                                    Identifier("rowData")).WithType(IdentifierName("LastModifiedClaimTableTypeRow")))
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
                .AddMembers(node.Definition.ColumnDefinitions.Select(c =>
                    (MemberDeclarationSyntax)PropertyDeclaration(
                            DataTypeReferenceToClrType(c.DataType, IsColumnNullable(c)),
                            Identifier(c.ColumnIdentifier.Value))
                        .AddModifiers(Token(SyntaxKind.InternalKeyword))
                        .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))).ToArray());

            MembersToAdd.Add(classDeclarationSyntax);
            MembersToAdd.Add(rowStruct);

            base.Visit(node);
        }

        private MemberDeclarationSyntax CreatePropertyForColumn(ColumnDefinition column)
        {
            string normalizedSqlDbType = Enum.Parse<SqlDbType>(column.DataType.Name.BaseIdentifier.Value, true).ToString();
            bool nullable = column.Constraints.Any(c => c is NullableConstraintDefinition nc && nc.Nullable);

            IdentifierNameSyntax typeName = IdentifierName($"{(nullable ? "Nullable" : string.Empty)}{normalizedSqlDbType}Column");

            return
                FieldDeclaration(
                        VariableDeclaration(typeName)
                            .AddVariables(VariableDeclarator($"{column.ColumnIdentifier.Value}")
                                .WithInitializer(
                                    EqualsValueClause(
                                        ObjectCreationExpression(typeName)
                                            .AddArgumentListArguments(
                                                Argument(
                                                    LiteralExpression(
                                                        SyntaxKind.StringLiteralExpression,
                                                        Literal(column.ColumnIdentifier.Value))))
                                            .AddArgumentListArguments(column.DataType is ParameterizedDataTypeReference parameterizedDataType
                                                ? parameterizedDataType.Parameters.Select(p => Argument(
                                                    LiteralExpression(
                                                        SyntaxKind.NumericLiteralExpression,
                                                        Literal(p.LiteralType == LiteralType.Max ? -1 : int.Parse(p.Value))))).ToArray()
                                                : Array.Empty<ArgumentSyntax>())))))
                    .AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword));
        }
    }
}
