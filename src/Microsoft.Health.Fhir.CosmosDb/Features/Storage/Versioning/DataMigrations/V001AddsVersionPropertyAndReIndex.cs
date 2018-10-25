// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations
{
    /// <summary>
    /// A migration that will ensure the version property exists on all documents
    /// </summary>
    internal class V001AddsVersionPropertyAndReindex : RebuildIndexMigration
    {
        public V001AddsVersionPropertyAndReindex(ISearchIndexer indexer)
            : base(indexer)
        {
        }

        public override int Version { get; } = 1;

        public override SqlQuerySpec DocumentsToMigrate()
        {
            // In this v1 update look for nodes that have no version defined.

            var spec = new SqlQuerySpec($@"SELECT * FROM c WHERE 
                                           (NOT IS_DEFINED(c.{KnownResourceWrapperProperties.IsSystem}) OR c.{KnownResourceWrapperProperties.IsSystem} = false)
                                           AND
                                           (NOT IS_DEFINED(c.{KnownResourceWrapperProperties.DataVersion}))");

            return spec;
        }

        public override IEnumerable<IUpdateOperation> Migrate(Document wrapper)
        {
            EnsureArg.IsNotNull(wrapper, nameof(wrapper));

            // Updates version property
            if (string.IsNullOrEmpty(wrapper.GetPropertyValue<string>(KnownResourceWrapperProperties.Version)))
            {
                yield return new UpdateOperation(KnownResourceWrapperProperties.Version, wrapper.ETag.Trim('"'));
            }

            // Updates search index
            foreach (var operation in base.Migrate(wrapper))
            {
                yield return operation;
            }
        }
    }
}
