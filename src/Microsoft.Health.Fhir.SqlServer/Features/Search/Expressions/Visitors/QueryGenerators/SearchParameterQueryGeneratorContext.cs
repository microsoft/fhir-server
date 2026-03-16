// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal readonly struct SearchParameterQueryGeneratorContext
    {
        internal SearchParameterQueryGeneratorContext(IndentedStringBuilder stringBuilder, HashingSqlQueryParameterManager parameters, ISqlServerFhirModel model, SchemaInformation schemaInformation, bool isAsyncOperation, string tableAlias = null)
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
    }
}
