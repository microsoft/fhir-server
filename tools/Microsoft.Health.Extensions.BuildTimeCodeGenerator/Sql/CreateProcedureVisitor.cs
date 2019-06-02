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
        private const string TvpGeneratorGenericTypeName = "TInput";
        private const string PopulateCommandMethodName = "PopulateCommand";
        private const string CommandParameterName = "command";

        public override int ArtifactSortOder => 1;

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
                    .AddMembers(AddPopulateCommandMethod(node, schemaQualifiedProcedureName), AddPopulateCommandMethodForTableValuedParameters(node, procedureName));

            FieldDeclarationSyntax fieldDeclarationSyntax = CreateStaticFieldForClass(className, procedureName);

            var (tvpGeneratorClass, tvpHolderStruct) = CreateTvpGeneratorTypes(node, procedureName);

            MembersToAdd.Add(classDeclarationSyntax.AddSortingKey(this, procedureName));
            MembersToAdd.Add(fieldDeclarationSyntax.AddSortingKey(this, procedureName));
            MembersToAdd.Add(tvpGeneratorClass.AddSortingKey(this, procedureName));
            MembersToAdd.Add(tvpHolderStruct.AddSortingKey(this, procedureName));

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
            if (TryGetSqlDbTypeForParameter(parameter, out SqlDbType sqlDbType))
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

                arguments.AddRange(GetDataTypeSpecificConstructorArguments(parameter.DataType, null));
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
                    Identifier(PopulateCommandMethodName))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))

                // first parameter is the SqlCommand
                .AddParameterListParameters(Parameter(Identifier(CommandParameterName)).WithType(typeof(SqlCommand).ToTypeSyntax(useGlobalAlias: true)))

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
                                IdentifierName(CommandParameterName),
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
                                IdentifierName(CommandParameterName),
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
                                IdentifierName(CommandParameterName),
                                IdentifierName("Parameters"))),
                            Argument(IdentifierName(ParameterNameForParameter(p)))))).ToArray());
        }

        private MemberDeclarationSyntax AddPopulateCommandMethodForTableValuedParameters(CreateProcedureStatement node, string procedureName)
        {
            var nonTableParameters = new List<ProcedureParameter>();
            var tableParameters = new List<ProcedureParameter>();

            foreach (var procedureParameter in node.Parameters)
            {
                if (TryGetSqlDbTypeForParameter(procedureParameter, out _))
                {
                    nonTableParameters.Add(procedureParameter);
                }
                else
                {
                    tableParameters.Add(procedureParameter);
                }
            }

            if (tableParameters.Count == 0)
            {
                return IncompleteMember();
            }

            string tableValuedParametersParameterName = "tableValuedParameters";

            return MethodDeclaration(
                    typeof(void).ToTypeSyntax(),
                    Identifier(PopulateCommandMethodName))
                .AddModifiers(Token(SyntaxKind.PublicKeyword))

                // first parameter is the SqlCommand
                .AddParameterListParameters(Parameter(Identifier(CommandParameterName)).WithType(typeof(SqlCommand).ToTypeSyntax(useGlobalAlias: true)))

                // Add a parameter for each non-TVP
                .AddParameterListParameters(nonTableParameters.Select(selector: p =>
                    Parameter(Identifier(ParameterNameForParameter(p)))
                        .WithType(DataTypeReferenceToClrType(p.DataType, p.Value != null))).ToArray())

                // Add a parameter for the TVP set
                .AddParameterListParameters(
                    Parameter(Identifier(tableValuedParametersParameterName)).WithType(IdentifierName(TableValuedParametersStructName(procedureName))))

                // Call the overload
                .AddBodyStatements(
                    ExpressionStatement(
                        InvocationExpression(
                                IdentifierName(PopulateCommandMethodName))
                            .AddArgumentListArguments(Argument(IdentifierName(CommandParameterName)))
                            .AddArgumentListArguments(
                                nonTableParameters.Select(p =>
                                    Argument(IdentifierName(ParameterNameForParameter(p)))
                                        .WithNameColon(NameColon(ParameterNameForParameter(p)))).ToArray())
                            .AddArgumentListArguments(
                                tableParameters.Select(p =>
                                    Argument(MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName(tableValuedParametersParameterName),
                                            IdentifierName(PropertyNameForParameter(p))))
                                        .WithNameColon(NameColon(ParameterNameForParameter(p)))).ToArray())));
        }

        private (MemberDeclarationSyntax tvpGeneratorClass, MemberDeclarationSyntax tvpHolderStruct) CreateTvpGeneratorTypes(CreateProcedureStatement node, string procedureName)
        {
            List<(string parameterName, string rowStructName)> rowTypes = node.Parameters
                .Where(p => !TryGetSqlDbTypeForParameter(p, out _))
                .Select(p => (parameterName: PropertyNameForParameter(p), rowStructName: GetRowStructNameForTableType(p.DataType.Name)))
                .ToList();

            if (rowTypes.Count == 0)
            {
                // no table-valued parameters on this procedure
                return (IncompleteMember(), IncompleteMember());
            }

            var holderStructName = TableValuedParametersStructName(procedureName);

            // create a struct with properties for each table-valued parameter

            var structDeclaration = StructDeclaration(holderStructName)
                .AddModifiers(Token(SyntaxKind.InternalKeyword))

                // Add a constructor with parameters for each column, setting the associated property for each column.
                .AddMembers(
                    ConstructorDeclaration(
                            Identifier(holderStructName))
                        .WithModifiers(
                            TokenList(
                                Token(SyntaxKind.InternalKeyword)))
                        .AddParameterListParameters(
                            rowTypes.Select(p =>
                                Parameter(Identifier(p.parameterName))
                                    .WithType(TypeExtensions.CreateGenericTypeFromGenericTypeDefinition(
                                        typeof(IEnumerable<>).ToTypeSyntax(true),
                                        IdentifierName(p.rowStructName)))).ToArray())
                        .WithBody(
                            Block(rowTypes.Select(p =>
                                ExpressionStatement(
                                    AssignmentExpression(
                                        SyntaxKind.SimpleAssignmentExpression,
                                        left: MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            ThisExpression(),
                                            IdentifierName(p.parameterName)),
                                        right: IdentifierName(p.parameterName)))))))

                // Add a property for each column
                .AddMembers(rowTypes.Select(p =>
                    (MemberDeclarationSyntax)PropertyDeclaration(
                            TypeExtensions.CreateGenericTypeFromGenericTypeDefinition(
                                typeof(IEnumerable<>).ToTypeSyntax(true),
                                IdentifierName(p.rowStructName)),
                            Identifier(p.parameterName))
                        .AddModifiers(Token(SyntaxKind.InternalKeyword))
                        .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))).ToArray());

            string className = $"{procedureName}TvpGenerator";

            List<string> distinctTvpTypeNames = rowTypes.Select(r => r.rowStructName).Distinct().ToList();

            var classDeclaration = ClassDeclaration(className)
                .AddTypeParameterListParameters(TypeParameter(TvpGeneratorGenericTypeName))
                .AddBaseListTypes(
                    SimpleBaseType(
                        GenericName("IStoredProcedureTableValuedParametersGenerator")
                            .AddTypeArgumentListArguments(
                                IdentifierName(TvpGeneratorGenericTypeName),
                                IdentifierName(holderStructName))))
                .AddModifiers(Token(SyntaxKind.InternalKeyword))
                .AddMembers(
                    ConstructorDeclaration(Identifier(className))
                        .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                        .AddParameterListParameters(distinctTvpTypeNames
                            .Select(t =>
                                Parameter(Identifier(GeneratorFieldName(t)))
                                    .WithType(GeneratorType(t))).ToArray())
                        .WithBody(
                            Block(distinctTvpTypeNames
                                .Select(t =>
                                    ExpressionStatement(
                                        AssignmentExpression(
                                            SyntaxKind.SimpleAssignmentExpression,
                                            left: MemberAccessExpression(
                                                SyntaxKind.SimpleMemberAccessExpression,
                                                ThisExpression(),
                                                IdentifierName(GeneratorFieldName(t))),
                                            right: IdentifierName(GeneratorFieldName(t))))))))
                .AddMembers(
                    distinctTvpTypeNames
                        .Select(t => (MemberDeclarationSyntax)FieldDeclaration(
                                VariableDeclaration(GeneratorType(t))
                                    .AddVariables(VariableDeclarator(GeneratorFieldName(t))))
                            .AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword))).ToArray())
                .AddMembers(
                    MethodDeclaration(IdentifierName(holderStructName), Identifier("Generate"))
                        .AddModifiers(Token(SyntaxKind.PublicKeyword))
                        .AddParameterListParameters(Parameter(Identifier("input")).WithType(IdentifierName(TvpGeneratorGenericTypeName)))
                        .AddBodyStatements(
                            ReturnStatement(
                                ObjectCreationExpression(IdentifierName(holderStructName))
                                    .AddArgumentListArguments(
                                        rowTypes.Select(p => Argument(
                                            InvocationExpression(
                                                    MemberAccessExpression(
                                                        SyntaxKind.SimpleMemberAccessExpression,
                                                        IdentifierName(GeneratorFieldName(p.rowStructName)),
                                                        IdentifierName("GenerateRows")))
                                                .AddArgumentListArguments(Argument(IdentifierName("input"))))).ToArray()))));

            return (classDeclaration, structDeclaration);
        }

        private static string TableValuedParametersStructName(string procedureName)
        {
            return $"{procedureName}TableValuedParameters";
        }

        private static string GeneratorFieldName(string tableTypeName)
        {
            return $"{tableTypeName}Generator";
        }

        private static TypeSyntax GeneratorType(string rowStructName)
        {
            return GenericName("ITableValuedParameterRowGenerator")
                .AddTypeArgumentListArguments(
                    IdentifierName(TvpGeneratorGenericTypeName),
                    IdentifierName(rowStructName));
        }

        private static bool TryGetSqlDbTypeForParameter(ProcedureParameter parameter, out SqlDbType sqlDbType)
        {
            return Enum.TryParse<SqlDbType>(parameter.DataType.Name.BaseIdentifier.Value, ignoreCase: true, out sqlDbType);
        }

        private static string ParameterNameForParameter(ProcedureParameter parameter)
        {
            return parameter.VariableName.Value.Substring(1);
        }

        private static string FieldNameForParameter(ProcedureParameter parameter)
        {
            return $"_{parameter.VariableName.Value.Substring(1)}";
        }

        private static string PropertyNameForParameter(ProcedureParameter parameter)
        {
            string parameterName = parameter.VariableName.Value;
            return $"{char.ToUpperInvariant(parameterName[1])}{(parameterName.Length > 2 ? parameterName.Substring(2) : string.Empty)}";
        }
    }
}
