// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    /// <summary>
    /// Represents a SQL table.
    /// </summary>
    public class Table
    {
        public Table(string tableName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(tableName, nameof(tableName));

            TableName = tableName;
        }

        public string TableName { get; }

        public static implicit operator string(Table t) => t.ToString();

        public override string ToString() => TableName;
    }
}
