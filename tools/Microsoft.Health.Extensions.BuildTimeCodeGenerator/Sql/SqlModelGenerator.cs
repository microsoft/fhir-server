// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

            var visitors = new SqlVisitor[] { new CreateTableVisitor(), new CreateProcedureVisitor(), new CreateTableTypeVisitor() };

            foreach (var sqlVisitor in visitors)
            {
                sqlFragment.Accept(sqlVisitor);
            }

            var classDeclaration = ClassDeclaration(typeName)
                .WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))
                .AddMembers(visitors
                    .SelectMany(v => v.MembersToAdd)
                    .OrderBy(m => m is FieldDeclarationSyntax ? 0 : m is ClassDeclarationSyntax ? 1 : 2)
                    .ThenBy(m =>
                    {
                        switch (m)
                        {
                            case FieldDeclarationSyntax f:
                                string fieldTypeName = f.Declaration.Type.ToString();
                                string variableName = f.Declaration.Variables.First().Identifier.Text;

                                return fieldTypeName.Substring(variableName.Length);
                            case ClassDeclarationSyntax c:
                                BaseTypeSyntax baseType = c.BaseList?.Types.FirstOrDefault();
                                if (baseType == null)
                                {
                                    return string.Empty;
                                }

                                return baseType.ToString();
                            case StructDeclarationSyntax s:
                                return s.Identifier.ToString();
                            default:
                                throw new NotSupportedException(m.GetType().Name);
                        }
                    })
                    .ThenBy(m =>
                    {
                        switch (m)
                        {
                            case FieldDeclarationSyntax f:
                                return f.Declaration.Type.ToString();
                            case ClassDeclarationSyntax c:
                                return c.Identifier.ToString();
                            case StructDeclarationSyntax _:
                                return string.Empty;
                            default:
                                throw new NotSupportedException(m.GetType().Name);
                        }
                    }).ToArray());

            return (classDeclaration, new UsingDirectiveSyntax[0]);
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
