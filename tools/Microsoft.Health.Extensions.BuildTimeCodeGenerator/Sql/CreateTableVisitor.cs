// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.Sql
{
    /// <summary>
    /// Visits a SQL AST, created a class for each CREATE TABLE statement.
    /// </summary>
    internal class CreateTableVisitor : TSqlFragmentVisitor
    {
        public List<(string name, ClassDeclarationSyntax classDeclaration)> Tables { get; } = new List<(string name, ClassDeclarationSyntax classDeclaration)>();

        public override void Visit(CreateTableStatement node)
        {
            string tableName = node.SchemaObjectName.BaseIdentifier.Value;
            string schemaQualifiedTableName = $"{node.SchemaObjectName.SchemaIdentifier.Value}.{tableName}";
            string className = $"{tableName}Table";

            ClassDeclarationSyntax classDeclarationSyntax =
                ClassDeclaration(className)
                    .WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))
                    .WithBaseList(
                        BaseList(
                            SingletonSeparatedList<BaseTypeSyntax>(
                                SimpleBaseType(
                                    IdentifierName("Table")))))
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
                                                    Literal(schemaQualifiedTableName)))))))
                            .WithBody(Block()))
                    .AddMembers(node.Definition.ColumnDefinitions.Select(CreatePropertyForColumn).ToArray());

            Tables.Add((tableName, classDeclarationSyntax));

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
                                        ObjectCreationExpression(
                                                typeName)
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
                    .AddModifiers(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.ReadOnlyKeyword));
        }
    }
}
