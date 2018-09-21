// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Azure.Documents;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations
{
    public abstract class Migration
    {
        public string Name => GetType().Name;

        public abstract SqlQuerySpec DocumentsToMigrate();

        public abstract IEnumerable<IUpdateOperation> Migrate(Document wrapper);
    }
}
