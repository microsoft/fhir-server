// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
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
    internal class CreateProcedureVisitor : TSqlFragmentVisitor
    {
        public List<(string name, ClassDeclarationSyntax classDeclaration)> Procedures { get; } = new List<(string name, ClassDeclarationSyntax classDeclaration)>();

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

            Procedures.Add((procedureName, classDeclarationSyntax));

            base.Visit(node);
        }

        /// <summary>
        /// Creates a Column-derived field for a stored procedure parameter.
        /// </summary>
        /// <param name="parameter">The stored procedure parameter</param>
        /// <returns>The field declaration</returns>
        private MemberDeclarationSyntax CreateFieldForParameter(ProcedureParameter parameter)
        {
            string normalizedSqlDbType = Enum.Parse<SqlDbType>(parameter.DataType.Name.BaseIdentifier.Value, true).ToString();
            IdentifierNameSyntax typeName = IdentifierName($"{(parameter.Value != null ? "Nullable" : string.Empty)}{normalizedSqlDbType}Column");

            return FieldDeclaration(
                    VariableDeclaration(typeName)

                        // call it "_myParam" when the parameter is named "@myParam"
                        .AddVariables(VariableDeclarator($"_{parameter.VariableName.Value.Substring(1)}")
                            .WithInitializer(
                                EqualsValueClause(
                                    ObjectCreationExpression(typeName)

                                        // the  first argument is the procedure name
                                        .AddArgumentListArguments(
                                            Argument(
                                                LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    Literal(parameter.VariableName.Value))))

                                        // next come data-type specific arguments, like length, precision, and scale
                                        .AddArgumentListArguments(parameter.DataType is ParameterizedDataTypeReference parameterizedDataType
                                            ? parameterizedDataType.Parameters.Select(p => Argument(
                                                LiteralExpression(
                                                    SyntaxKind.NumericLiteralExpression,
                                                    Literal(p.LiteralType == LiteralType.Max ? -1 : int.Parse(p.Value))))).ToArray()
                                            : Array.Empty<ArgumentSyntax>())))))
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
                        .WithType(DataTypeReferenceToClrType(p.DataType, p.Value != null).ToTypeSyntax())).ToArray())

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
                // command.Parameter.AddFromColumn(_fieldForParameter, valueParameter, "@myParam");
                .AddBodyStatements(node.Parameters.Select(p => (StatementSyntax)ExpressionStatement(
                    InvocationExpression(
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName("command"),
                                    IdentifierName("Parameters")),
                                IdentifierName("AddFromColumn")))
                        .AddArgumentListArguments(
                            Argument(IdentifierName($"_{p.VariableName.Value.Substring(1)}")),
                            Argument(IdentifierName(p.VariableName.Value.Substring(1))),
                            Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(p.VariableName.Value)))))).ToArray());
        }

        private static Type DataTypeReferenceToClrType(DataTypeReference reference, bool nullable)
        {
            Type type = DataTypeReferenceToClrType(reference);

            if (nullable && !type.IsClass)
            {
                return typeof(Nullable<>).MakeGenericType(type);
            }

            return type;
        }

        private static Type DataTypeReferenceToClrType(DataTypeReference reference)
        {
            var sqlDbType = Enum.Parse<SqlDbType>(reference.Name.BaseIdentifier.Value, true);
            switch (sqlDbType)
            {
                case SqlDbType.BigInt:
                    return typeof(long);
                case SqlDbType.Binary:
                    break;
                case SqlDbType.Bit:
                    return typeof(bool);
                case SqlDbType.Char:
                    break;
                case SqlDbType.Date:
                case SqlDbType.DateTime:
                case SqlDbType.DateTime2:
                case SqlDbType.SmallDateTime:
                case SqlDbType.Time:
                    return typeof(DateTime);
                case SqlDbType.DateTimeOffset:
                    return typeof(DateTimeOffset);
                case SqlDbType.Decimal:
                    return typeof(decimal);
                case SqlDbType.Float:
                    break;
                case SqlDbType.Image:
                    break;
                case SqlDbType.Int:
                    return typeof(int);
                case SqlDbType.Money:
                    break;
                case SqlDbType.NChar:
                case SqlDbType.NText:
                case SqlDbType.VarChar:
                case SqlDbType.NVarChar:
                case SqlDbType.Text:
                    return typeof(string);
                case SqlDbType.Real:
                    return typeof(double);
                case SqlDbType.SmallInt:
                    return typeof(short);
                case SqlDbType.SmallMoney:
                    break;
                case SqlDbType.Structured:
                    return typeof(object);
                case SqlDbType.Timestamp:
                    return typeof(byte[]);
                case SqlDbType.TinyInt:
                    return typeof(byte);
                case SqlDbType.Udt:
                    break;
                case SqlDbType.UniqueIdentifier:
                    return typeof(Guid);
                case SqlDbType.VarBinary:
                    return typeof(Stream);
                case SqlDbType.Variant:
                    break;
                case SqlDbType.Xml:
                    break;
            }

            throw new NotSupportedException(sqlDbType.ToString());
        }
    }
}
