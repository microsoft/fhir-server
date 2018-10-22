// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning.DataMigrations
{
    /// <summary>
    /// A migration that will ensure the version property exists on all documents
    /// </summary>
    internal class V001AddsVersionPropertyToAllDocuments : Migration
    {
        public override int Version { get; } = 1;

        public override IEnumerable<IUpdateOperation> Migrate(Document wrapper)
        {
            EnsureArg.IsNotNull(wrapper, nameof(wrapper));

            if (string.IsNullOrEmpty(wrapper.GetPropertyValue<string>(KnownResourceWrapperProperties.Version)))
            {
                yield return new UpdateOperation(KnownResourceWrapperProperties.Version, wrapper.ETag.Trim('"'));
            }
        }
    }
}
