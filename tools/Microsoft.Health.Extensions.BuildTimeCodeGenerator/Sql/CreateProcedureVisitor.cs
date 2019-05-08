// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
            List<ArgumentSyntax> arguments = new List<ArgumentSyntax>
            {
                Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(parameter.VariableName.Value))),
            };
            if (Enum.TryParse<SqlDbType>(parameter.DataType.Name.BaseIdentifier.Value, ignoreCase: true, out var sqlDbType))
            {
                // new ParameterDefinition<int>("@paramName", SqlDbType.Int, nullable, maxlength,...)
                typeName = GenericName("ParameterDefinition")
                    .AddTypeArgumentListArguments(SqlDbTypeToClrType(sqlDbType, nullable: parameter.Value != null).ToTypeSyntax(true));

                arguments.Add(
                    Argument(
                        MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            typeof(SqlDbType).ToTypeSyntax(true),
                            IdentifierName(sqlDbType.ToString()))));
                arguments.Add(
                    Argument(
                        LiteralExpression(parameter.Value == null ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression)));

                arguments.AddRange(GetDataTypeSpecificConstructorArguments(parameter.DataType));
            }
            else
            {
                // new MyTableValuedParameterDefinition("@paramName");
                typeName = IdentifierName(GetClassNameForTableValuedParameterDefinition(parameter.DataType.Name));
            }

            return FieldDeclaration(
                    VariableDeclaration(typeName)

                        // call it "_myParam" when the parameter is named "@myParam"
                        .AddVariables(VariableDeclarator(FieldNameForParameter(parameter))
                            .WithInitializer(
                                EqualsValueClause(
                                    ObjectCreationExpression(typeName)
                                        .AddArgumentListArguments(arguments.ToArray())))))
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
                    Parameter(Identifier(ParameterNameForParameter(p)))
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
                // _fieldForParameter.AddParameter(command, parameterValue)
                .AddBodyStatements(node.Parameters.Select(p => (StatementSyntax)ExpressionStatement(
                    InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName(FieldNameForParameter(p)),
                                IdentifierName("AddParameter")))
                        .AddArgumentListArguments(
                            Argument(MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                IdentifierName("command"),
                                IdentifierName("Parameters"))),
                            Argument(IdentifierName(ParameterNameForParameter(p)))))).ToArray());
        }

        private static string ParameterNameForParameter(ProcedureParameter parameter)
        {
            return parameter.VariableName.Value.Substring(1);
        }

        private static string FieldNameForParameter(ProcedureParameter parameter)
        {
            return $"_{parameter.VariableName.Value.Substring(1)}";
        }
    }
}
