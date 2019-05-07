// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Linq;
using EnsureThat;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.Sql
{
    /// <summary>
    /// Generates a C# class based on the the CREATE TABLE and CREATE PROCEDURE statements in a .sql file
    /// </summary>
    public class SqlModelGenerator : ICodeGenerator
    {
        private readonly string _sqlFile;

        public SqlModelGenerator(string[] args)
        {
            EnsureArg.IsNotNull(args, nameof(args));
            EnsureArg.SizeIs(args, 1, nameof(args));

            _sqlFile = args[0];
        }

        public (MemberDeclarationSyntax, UsingDirectiveSyntax[]) Generate(string typeName)
        {
            TSqlFragment sqlFragment = ParseSqlFile();

            var createTableVisitor = new CreateTableVisitor();
            sqlFragment.Accept(createTableVisitor);

            var createProcedureVisitor = new CreateProcedureVisitor();
            sqlFragment.Accept(createProcedureVisitor);

            var classDeclaration = ClassDeclaration(typeName)
                .WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))

                // add fields for each table
                .AddMembers(
                    createTableVisitor.Tables.Select(t =>
                        FieldDeclaration(
                                VariableDeclaration(IdentifierName(t.classDeclaration.Identifier.Text))
                                    .AddVariables(VariableDeclarator(t.name)
                                        .WithInitializer(
                                            EqualsValueClause(
                                                ObjectCreationExpression(
                                                    IdentifierName(t.classDeclaration.Identifier.Text)).AddArgumentListArguments()))))
                            .AddModifiers(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.ReadOnlyKeyword), Token(SyntaxKind.StaticKeyword))).Cast<MemberDeclarationSyntax>().ToArray())

                // add fields for each stored procedure
                .AddMembers(
                    createProcedureVisitor.Procedures.Select(t =>
                        FieldDeclaration(
                                VariableDeclaration(IdentifierName(t.classDeclaration.Identifier.Text))
                                    .AddVariables(VariableDeclarator(t.name)
                                        .WithInitializer(
                                            EqualsValueClause(
                                                ObjectCreationExpression(
                                                    IdentifierName(t.classDeclaration.Identifier.Text)).AddArgumentListArguments()))))
                            .AddModifiers(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.ReadOnlyKeyword), Token(SyntaxKind.StaticKeyword))).Cast<MemberDeclarationSyntax>().ToArray())

                // add inner classes for tables and procedures
                .AddMembers(
                    createTableVisitor.Tables.Concat(createProcedureVisitor.Procedures).Select(t => (MemberDeclarationSyntax)t.classDeclaration).ToArray());

            var usingDirectiveSyntax = UsingDirective(
                    "Microsoft.Health.Fhir.SqlServer.Features.Storage"
                        .Split('.')
                        .Select(s => (NameSyntax)IdentifierName(s))
                        .Aggregate((acc, id) => QualifiedName(acc, (SimpleNameSyntax)id)));

            return (classDeclaration, new[] { usingDirectiveSyntax });
        }

        private TSqlFragment ParseSqlFile()
        {
            using (var stream = File.OpenRead(_sqlFile))
            using (var reader = new StreamReader(stream))
            {
                var parser = new TSql150Parser(true);
                return parser.Parse(reader, out var errors);
            }
        }
    }
}
