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
    public abstract class SqlVisitor : TSqlFragmentVisitor
    {
        public List<MemberDeclarationSyntax> MembersToAdd { get; } = new List<MemberDeclarationSyntax>();

        protected static TypeSyntax DataTypeReferenceToClrType(DataTypeReference reference, bool nullable)
        {
            if (Enum.TryParse<SqlDbType>(reference.Name.BaseIdentifier.Value, ignoreCase: true, out var sqlDbType))
            {
                return SqlDbTypeToClrType(sqlDbType, nullable).ToTypeSyntax(useGlobalAlias: true);
            }

            // assumed to be a table type

            TypeSyntax enumerableType = typeof(IEnumerable<>).ToTypeSyntax(true);
            GenericNameSyntax openGeneric = enumerableType.DescendantNodes().OfType<GenericNameSyntax>().Single();
            GenericNameSyntax closedGeneric = openGeneric.AddTypeArgumentListArguments(SyntaxFactory.IdentifierName($"{reference.Name.BaseIdentifier.Value}Row"));

            return enumerableType.ReplaceNode(openGeneric, closedGeneric);
        }

        protected static Type SqlDbTypeToClrType(SqlDbType sqlDbType, bool nullable)
        {
            Type type = SqlDbTypeToClrType(sqlDbType);
            if (nullable && !type.IsClass)
            {
                type = typeof(Nullable<>).MakeGenericType(type);
            }

            return type;
        }

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

        protected static bool IsColumnNullable(ColumnDefinition column)
        {
            return column.Constraints.Any(c => c is NullableConstraintDefinition nc && nc.Nullable);
        }
    }
}
