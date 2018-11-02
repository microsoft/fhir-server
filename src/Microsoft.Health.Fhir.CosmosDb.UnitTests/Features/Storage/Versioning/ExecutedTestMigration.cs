// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Versioning
{
    public class ExecutedTestMigration : Migration
    {
        public override int Version { get; } = int.MaxValue;

        public override SqlQuerySpec DocumentsToMigrate()
        {
            throw new System.NotImplementedException();
        }

        public override IEnumerable<IUpdateOperation> Migrate(Document wrapper)
        {
            throw new System.NotImplementedException();
        }
    }
}
