// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations
{
    public abstract class Migration
    {
        public string Name => GetType().Name;

        public abstract int Version { get; }

        public virtual SqlQuerySpec DocumentsToMigrate()
        {
            var spec = new SqlQuerySpec($@"SELECT * FROM c WHERE 
                                           (NOT IS_DEFINED(c.{KnownResourceWrapperProperties.IsSystem}) OR c.{KnownResourceWrapperProperties.IsSystem} = false)
                                           AND
                                           (NOT IS_DEFINED(c.{KnownResourceWrapperProperties.DataVersion}) OR c.{KnownResourceWrapperProperties.DataVersion} < @version)");

            spec.Parameters.Add(new SqlParameter("@version", Version));

            return spec;
        }

        public abstract IEnumerable<IUpdateOperation> Migrate(Document wrapper);
    }
}
