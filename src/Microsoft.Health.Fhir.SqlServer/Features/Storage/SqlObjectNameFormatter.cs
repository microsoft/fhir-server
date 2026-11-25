// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Qualifies physical SQL object identifiers without changing aliases or query-local tables.
    /// </summary>
    public sealed class SqlObjectNameFormatter
    {
        private const string IdentifierPattern = @"(?:\[(?:[^\]]|\]\])+\]|""(?:[^""]|"""")+""|[\p{L}_@#][\p{L}\p{N}_@$#]*)";
        private static readonly Regex ProcedureNamePattern = new Regex(
            @"^\s*(?<part>" + IdentifierPattern + @")(?:\s*\.\s*(?<part>" + IdentifierPattern + @")){0,2}\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string _databasePrefix;

        /// <summary>
        /// Initializes a formatter for a database, or legacy table names when the database is null.
        /// </summary>
        public SqlObjectNameFormatter(string databaseName)
        {
            DatabaseName = databaseName;
            _databasePrefix = databaseName == null ? null : QuoteIdentifier(databaseName) + ".";
        }

        internal static SqlObjectNameFormatter ApplicationDatabase { get; } = new SqlObjectNameFormatter(null);

        internal string DatabaseName { get; }

        internal void PrepareCommand(SqlCommand command)
        {
            if (command.CommandType == CommandType.StoredProcedure)
            {
#pragma warning disable CA2100 // Only registry database names and parsed, quoted SQL identifiers are emitted.
                command.CommandText = GetStoredProcedureName(command.CommandText);
#pragma warning restore CA2100
            }
        }

        /// <summary>
        /// Gets the table identifier with each component safely quoted when a database is configured.
        /// </summary>
        public string GetTableName(Table table) => GetObjectName(table.TableName);

        /// <summary>
        /// Gets a schema-qualified object name, such as <c>dbo.Parameters</c> or <c>sys.objects</c>, with each component
        /// safely quoted and prefixed with the database when a database is configured.
        /// </summary>
        internal string GetObjectName(string schemaQualifiedName) =>
            _databasePrefix == null
                ? schemaQualifiedName
                : _databasePrefix + string.Join(".", schemaQualifiedName.Split('.').Select(QuoteIdentifier));

        /// <summary>
        /// Gets a database-qualified procedure name, defaulting an omitted schema to dbo.
        /// </summary>
        public string GetStoredProcedureName(string procedureName)
        {
            if (DatabaseName == null)
            {
                return procedureName;
            }

            var match = ProcedureNamePattern.Match(procedureName);
            if (!match.Success)
            {
                throw new ArgumentException("Expected a one-, two-, or three-part stored procedure identifier.", nameof(procedureName));
            }

            var parts = match.Groups["part"].Captures.Select(capture => UnquoteIdentifier(capture.Value)).ToArray();
            if (parts.Length == 3 && !string.Equals(parts[0], DatabaseName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The stored procedure database does not match the request partition database.");
            }

            string schema = parts.Length == 1 ? "dbo" : parts[^2];
            return _databasePrefix + QuoteIdentifier(schema) + "." + QuoteIdentifier(parts[^1]);
        }

        private static string UnquoteIdentifier(string value) =>
            value[0] == '[' ? value[1..^1].Replace("]]", "]", StringComparison.Ordinal) :
            value[0] == '"' ? value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal) : value;

        private static string QuoteIdentifier(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }
}
