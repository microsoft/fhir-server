// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public interface ISqlImportOperation
    {
        /// <summary>
        /// Merge resources to resource and search param tables.
        /// </summary>
        /// <param name="resources">Input resources content.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task<IEnumerable<ImportResource>> MergeResourcesAsync(IEnumerable<ImportResource> resources, CancellationToken cancellationToken);
    }
}
