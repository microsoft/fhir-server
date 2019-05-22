// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    internal class CreateTableVisitor : SqlVisitor
    {
        public override int ArtifactSortOder => 0;

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

            FieldDeclarationSyntax field = CreateStaticFieldForClass(className, tableName);

            MembersToAdd.Add(field.AddSortingKey(this, tableName));
            MembersToAdd.Add(classDeclarationSyntax.AddSortingKey(this, tableName));

            base.Visit(node);
        }
    }
}
