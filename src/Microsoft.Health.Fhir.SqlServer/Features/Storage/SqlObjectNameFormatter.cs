// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Qualifies physical SQL object identifiers without changing aliases or query-local tables.
    /// </summary>
    internal sealed class SqlObjectNameFormatter
    {
        private const string IdentifierPattern = @"(?:\[(?:[^\]]|\]\])+\]|""(?:[^""]|"""")+""|[\p{L}_@#][\p{L}\p{N}_@$#]*)";
        private static readonly Regex ProcedureNamePattern = new Regex(
            @"^\s*(?<part>" + IdentifierPattern + @")(?:\s*\.\s*(?<part>" + IdentifierPattern + @")){0,2}\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string _databaseName;
        private readonly string _databasePrefix;

        /// <summary>
        /// Initializes a formatter for a database, or legacy table names when the database is null.
        /// </summary>
        public SqlObjectNameFormatter(string databaseName)
        {
            _databaseName = databaseName;
            _databasePrefix = databaseName == null ? null : QuoteIdentifier(databaseName) + ".";
        }

        /// <summary>
        /// Gets the table identifier with each component safely quoted when a database is configured.
        /// </summary>
        public string GetTableName(Table table) =>
            _databasePrefix == null
                ? table.TableName
                : _databasePrefix + string.Join(".", table.TableName.Split('.').Select(QuoteIdentifier));

        /// <summary>
        /// Gets a database-qualified procedure name, defaulting an omitted schema to dbo.
        /// </summary>
        public string GetStoredProcedureName(string procedureName)
        {
            if (_databaseName == null)
            {
                return procedureName;
            }

            var match = ProcedureNamePattern.Match(procedureName);
            if (!match.Success)
            {
                throw new ArgumentException("Expected a one-, two-, or three-part stored procedure identifier.", nameof(procedureName));
            }

            var parts = match.Groups["part"].Captures.Select(capture => UnquoteIdentifier(capture.Value)).ToArray();
            if (parts.Length == 3 && !string.Equals(parts[0], _databaseName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The stored procedure database does not match the request tenant database.");
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
