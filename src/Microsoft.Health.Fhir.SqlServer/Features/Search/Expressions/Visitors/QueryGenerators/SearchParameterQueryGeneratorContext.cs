// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal readonly struct SearchParameterQueryGeneratorContext
    {
        private readonly SqlTableNameFormatter _tableNames;

        internal SearchParameterQueryGeneratorContext(IndentedStringBuilder stringBuilder, HashingSqlQueryParameterManager parameters, ISqlServerFhirModel model, SchemaInformation schemaInformation, bool isAsyncOperation, string tableAlias = null, SqlTableNameFormatter tableNames = null)
        {
            EnsureArg.IsNotNull(stringBuilder, nameof(stringBuilder));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            StringBuilder = stringBuilder;
            Parameters = parameters;
            Model = model;
            SchemaInformation = schemaInformation;
            TableAlias = tableAlias;
            IsAsyncOperation = isAsyncOperation;
            _tableNames = tableNames ?? new SqlTableNameFormatter(null);
        }

        public IndentedStringBuilder StringBuilder { get; }

        public HashingSqlQueryParameterManager Parameters { get; }

        public ISqlServerFhirModel Model { get; }

        public SchemaInformation SchemaInformation { get; }

        /// <summary>
        /// Flag for async operations.
        /// </summary>
        public bool IsAsyncOperation { get; }

        public string TableAlias { get; }

        /// <summary>
        /// Gets the physical table identifier for the current query's database.
        /// </summary>
        public string GetTableName(Table table) => _tableNames.GetTableName(table);
    }
}
