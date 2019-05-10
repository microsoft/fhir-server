// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.Sql
{
    /// <summary>
    /// The base class for SQL visitors
    /// </summary>
    public abstract class SqlVisitor : TSqlFragmentVisitor
    {
        /// <summary>
        /// These members will be added to the generated class.
        /// </summary>
        public List<MemberDeclarationSyntax> MembersToAdd { get; } = new List<MemberDeclarationSyntax>();

        /// <summary>
        /// Converts a <see cref="DataTypeReference"/> to a <see cref="TypeSyntax"/>
        /// </summary>
        /// <param name="dataType">The type</param>
        /// <param name="nullable">Whether the type is nullable</param>
        /// <returns>The <see cref="TypeSyntax"/></returns>
        protected static TypeSyntax DataTypeReferenceToClrType(DataTypeReference dataType, bool nullable)
        {
            if (Enum.TryParse<SqlDbType>(dataType.Name.BaseIdentifier.Value, ignoreCase: true, out var sqlDbType))
            {
                return SqlDbTypeToClrType(sqlDbType, nullable).ToTypeSyntax(useGlobalAlias: true);
            }

            // assumed to be a table type

            return TypeExtensions.CreateGenericTypeFromGenericTypeDefinition(
                typeof(IEnumerable<>).ToTypeSyntax(true),
                SyntaxFactory.IdentifierName(GetRowStructNameForTableType(dataType.Name)));
        }

        /// <summary>
        /// Converts a <see cref="SqlDbType"/> to a <see cref="Type"/>.
        /// </summary>
        /// <param name="sqlDbType">The type</param>
        /// <param name="nullable">Whether the type is nullable</param>
        /// <returns>The <see cref="Type"/></returns>
        protected static Type SqlDbTypeToClrType(SqlDbType sqlDbType, bool nullable)
        {
            Type type = SqlDbTypeToClrType(sqlDbType);
            if (nullable && !type.IsClass)
            {
                type = typeof(Nullable<>).MakeGenericType(type);
            }

            return type;
        }

        /// <summary>
        /// Converts a <see cref="SqlDbType"/> to a <see cref="Type"/>.
        /// </summary>
        /// <param name="sqlDbType">The type</param>
        /// <returns>The <see cref="Type"/></returns>
        protected static Type SqlDbTypeToClrType(SqlDbType sqlDbType)
        {
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

        /// <summary>
        /// Determines whether the given <see cref="ColumnDefinition"/> is nullable
        /// </summary>
        /// <param name="column">The column</param>
        /// <returns>Whether the column is nullable</returns>
        protected static bool IsColumnNullable(ColumnDefinition column)
        {
            return column.Constraints.Any(c => c is NullableConstraintDefinition nc && nc.Nullable);
        }

        /// <summary>
        /// Creates an internal static readonly field for an instance of a class that has a parameterless constructor
        /// </summary>
        /// <param name="className">The class name</param>
        /// <param name="fieldName">The field name</param>
        /// <returns>The field.</returns>
        protected static FieldDeclarationSyntax CreateStaticFieldForClass(string className, string fieldName)
        {
            return SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName(className))
                        .AddVariables(SyntaxFactory.VariableDeclarator(fieldName)
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.ObjectCreationExpression(
                                        SyntaxFactory.IdentifierName(className)).AddArgumentListArguments()))))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.InternalKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword));
        }

        /// <summary>
        /// Gets the arguments for SQL database types, like the length, precision, and scale.
        /// </summary>
        /// <param name="dataType">The column data type</param>
        /// <returns>The type arguments</returns>
        protected static IEnumerable<ArgumentSyntax> GetDataTypeSpecificConstructorArguments(DataTypeReference dataType)
        {
            if (dataType is ParameterizedDataTypeReference parameterizedDataType)
            {
                return parameterizedDataType.Parameters.Select(p => SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(p.LiteralType == LiteralType.Max ? -1 : int.Parse(p.Value)))));
            }

            return Array.Empty<ArgumentSyntax>();
        }

        /// <summary>
        /// Gets the name of the class that derives from TableValuedParameterDefiniition for a table type.
        /// </summary>
        /// <param name="objectName">The name of the table type</param>
        /// <returns>The class name</returns>
        protected static string GetClassNameForTableValuedParameterDefinition(SchemaObjectName objectName)
        {
            return $"{objectName.BaseIdentifier.Value}TableValuedParameterDefinition";
        }

        /// <summary>
        /// Gets the name of the row struct for a table type.
        /// </summary>
        /// <param name="objectName">The name of the table type</param>
        /// <returns>The struct name</returns>
        protected static string GetRowStructNameForTableType(SchemaObjectName objectName)
        {
            return $"{objectName.BaseIdentifier.Value}Row";
        }

        protected MemberDeclarationSyntax CreatePropertyForColumn(ColumnDefinition column)
        {
            string normalizedSqlDbType = Enum.Parse<SqlDbType>(column.DataType.Name.BaseIdentifier.Value, true).ToString();

            IdentifierNameSyntax typeName = SyntaxFactory.IdentifierName($"{(IsColumnNullable(column) ? "Nullable" : string.Empty)}{normalizedSqlDbType}Column");

            return SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(typeName)
                        .AddVariables(SyntaxFactory.VariableDeclarator($"{column.ColumnIdentifier.Value}")
                            .WithInitializer(
                                SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.ObjectCreationExpression(
                                            typeName)
                                        .AddArgumentListArguments(
                                            SyntaxFactory.Argument(
                                                SyntaxFactory.LiteralExpression(
                                                    SyntaxKind.StringLiteralExpression,
                                                    SyntaxFactory.Literal(column.ColumnIdentifier.Value))))
                                        .AddArgumentListArguments(GetDataTypeSpecificConstructorArguments(column.DataType).ToArray())))))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.InternalKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));
        }
    }
}
