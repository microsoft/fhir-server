// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// Qualifies physical table identifiers without changing aliases or query-local tables.
    /// </summary>
    internal sealed class SqlTableNameFormatter
    {
        private readonly string _databasePrefix;

        /// <summary>
        /// Initializes a formatter for a database, or legacy table names when the database is null.
        /// </summary>
        public SqlTableNameFormatter(string databaseName)
        {
            _databasePrefix = databaseName == null ? null : QuoteIdentifier(databaseName) + ".";
        }

        /// <summary>
        /// Gets the table identifier with each component safely quoted when a database is configured.
        /// </summary>
        public string GetTableName(Table table) =>
            _databasePrefix == null
                ? table.TableName
                : _databasePrefix + string.Join(".", table.TableName.Split('.').Select(QuoteIdentifier));

        private static string QuoteIdentifier(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }
}
