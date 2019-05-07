// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.Sql
{
    /// <summary>
    /// Visits a SQL AST, creating a class for each CREATE PROCEDURE statement. Classes have a PopulateCommand method, with a signature derived from the procedure's signature.
    /// </summary>
    internal class CreateProcedureVisitor : SqlVisitor
    {
        public override void Visit(CreateProcedureStatement node)
        {
            string procedureName = node.ProcedureReference.Name.BaseIdentifier.Value;
            string schemaQualifiedProcedureName = $"{node.ProcedureReference.Name.SchemaIdentifier.Value}.{procedureName}";
            string className = $"{procedureName}Procedure";

            ClassDeclarationSyntax classDeclarationSyntax =
                ClassDeclaration(className)
                    .WithModifiers(TokenList(Token(SyntaxKind.InternalKeyword)))

                    // derive from StoredProcedure
                    .WithBaseList(
                        BaseList(
                            SingletonSeparatedList<BaseTypeSyntax>(
                                SimpleBaseType(
                                    IdentifierName("StoredProcedure")))))

                    // call base("dbo.StoredProcedure")
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
                                                    Literal(schemaQualifiedProcedureName)))))))
                            .WithBody(Block()))

                    // add fields for each parameter
                    .AddMembers(node.Parameters.Select(CreateFieldForParameter).ToArray())

                    // add the PopulateCommand method
                    .AddMembers(AddPopulateCommandMethod(node, schemaQualifiedProcedureName));

            FieldDeclarationSyntax fieldDeclarationSyntax = CreateStaticFieldForClass(className, procedureName);

            MembersToAdd.Add(classDeclarationSyntax);
            MembersToAdd.Add(fieldDeclarationSyntax);

            base.Visit(node);
        }

        /// <summary>
        /// Creates a Column-derived field for a stored procedure parameter.
        /// </summary>
        /// <param name="parameter">The stored procedure parameter</param>
        /// <returns>The field declaration</returns>
        private MemberDeclarationSyntax CreateFieldForParameter(ProcedureParameter parameter)
        {
            TypeSyntax typeName;
            ArgumentSyntax[] arguments;
            if (Enum.TryParse<SqlDbType>(parameter.DataType.Name.BaseIdentifier.Value, ignoreCase: true, out var sqlDbType))
            {
                typeName = GenericName("ParameterDefinition")
                    .AddTypeArgumentListArguments(SqlDbTypeToClrType(sqlDbType, nullable: parameter.Value != null).ToTypeSyntax(true));

                arguments = new[]
                {
                    Argument(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            typeof(SqlDbType).ToTypeSyntax(true),
                            IdentifierName(sqlDbType.ToString()))),
                    Argument(LiteralExpression(parameter.Value == null ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)),
                }.Concat(GetDataTypeSpecificConstructorArguments(parameter.DataType)).ToArray();
            }
            else
            {
                typeName = IdentifierName(GetTableValueParameterDefinitionDerivedClassName(parameter.DataType.Name));
                arguments = new ArgumentSyntax[0];
            }

            return FieldDeclaration(
                    VariableDeclaration(typeName)

                        // call it "_myParam" when the parameter is named "@myParam"
                        .AddVariables(VariableDeclarator($"_{parameter.VariableName.Value.Substring(1)}")
                            .WithInitializer(
                                EqualsValueClause(
                                    ObjectCreationExpression(typeName)
                                        .AddArgumentListArguments(arguments)))))
                .AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword));
        }

        /// <summary>
        /// Creates a PopulateCommand method taking a SqlCommand and parameters for each sproc parameter.
        /// </summary>
        /// <param name="node">The CREATE STORED PROCEDURE statement</param>
        /// <param name="schemaQualifiedProcedureName">The full name of the stored procedure</param>
        /// <returns>The method declaration</returns>
        private MethodDeclarationSyntax AddPopulateCommandMethod(CreateProcedureStatement node, string schemaQualifiedProcedureName)
        {
            return MethodDeclaration(
                    typeof(void).ToTypeSyntax(),
                    Identifier("PopulateCommand"))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))

                // first parameter is the SqlCommand
                .AddParameterListParameters(Parameter(Identifier("command")).WithType(typeof(SqlCommand).ToTypeSyntax(useGlobalAlias: true)))

                // Add a parameter for each stored procedure parameter
                .AddParameterListParameters(node.Parameters.Select(selector: p =>
                    Parameter(Identifier(p.VariableName.Value.Substring(1)))
                        .WithType(DataTypeReferenceToClrType(p.DataType, p.Value != null))).ToArray())

                // start the body with:
                // command.CommandType = CommandType.StoredProcedure
                // command.CommandText = "dbo.MySproc"
                .AddBodyStatements(
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("command"),
                                IdentifierName("CommandType")),
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                typeof(CommandType).ToTypeSyntax(useGlobalAlias: true),
                                IdentifierName("StoredProcedure")))),
                    ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("command"),
                                IdentifierName("CommandText")),
                            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(schemaQualifiedProcedureName)))))

                // now for each parameter generate:
                // _fieldForParameter.AddParameter(command, parameterValue, @"myParam")
                .AddBodyStatements(node.Parameters.Select(p => (StatementSyntax)ExpressionStatement(
                    InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName($"_{p.VariableName.Value.Substring(1)}"),
                                IdentifierName("AddParameter")))
                        .AddArgumentListArguments(
                            Argument(MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("command"),
                                IdentifierName("Parameters"))),
                            Argument(IdentifierName(p.VariableName.Value.Substring(1))),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(p.VariableName.Value)))))).ToArray());
        }
    }
}
