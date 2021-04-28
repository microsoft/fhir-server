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
        internal SearchParameterQueryGeneratorContext(IndentedStringBuilder stringBuilder, HashingSqlQueryParameterManager parameters, ISqlServerFhirModel model, SchemaInformation schemaInformation, string tableAlias, Column resourceTypeIdColumn, Column resourceSurrogateIdColumn)
        {
            EnsureArg.IsNotNull(stringBuilder, nameof(stringBuilder));
            EnsureArg.IsNotNull(parameters, nameof(parameters));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(resourceTypeIdColumn, nameof(resourceTypeIdColumn));
            EnsureArg.IsNotNull(resourceSurrogateIdColumn, nameof(resourceSurrogateIdColumn));

            StringBuilder = stringBuilder;
            Parameters = parameters;
            Model = model;
            SchemaInformation = schemaInformation;
            TableAlias = tableAlias;
            ResourceTypeIdColumn = resourceTypeIdColumn;
            ResourceSurrogateIdColumn = resourceSurrogateIdColumn;
        }

        public IndentedStringBuilder StringBuilder { get; }

        public HashingSqlQueryParameterManager Parameters { get; }

        public ISqlServerFhirModel Model { get; }

        public SchemaInformation SchemaInformation { get; }

        public string TableAlias { get; }

        public Column ResourceTypeIdColumn { get; }

        public Column ResourceSurrogateIdColumn { get; }
    }
}
