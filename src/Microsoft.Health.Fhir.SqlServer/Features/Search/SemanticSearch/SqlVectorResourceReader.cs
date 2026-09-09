// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch
{
    /// <summary>
    /// Reads persisted resources needed to resolve vector source text without depending on the FHIR data store.
    /// </summary>
    internal sealed class SqlVectorResourceReader : IVectorResourceReader
    {
        private readonly SqlStoreClient _storeClient;
        private readonly SqlServerFhirModel _model;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;

        public SqlVectorResourceReader(
            SqlStoreClient storeClient,
            SqlServerFhirModel model,
            ICompressedRawResourceConverter compressedRawResourceConverter)
        {
            _storeClient = EnsureArg.IsNotNull(storeClient, nameof(storeClient));
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
        }

        /// <inheritdoc />
        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(key, nameof(key));

            IReadOnlyList<ResourceWrapper> resources = await _storeClient.GetAsync(
                new[] { key },
                _model.GetResourceTypeId,
                _compressedRawResourceConverter.ReadCompressedRawResource,
                _model.GetResourceTypeName,
                isReadOnly: true,
                cancellationToken);

            return resources.SingleOrDefault();
        }
    }
}
