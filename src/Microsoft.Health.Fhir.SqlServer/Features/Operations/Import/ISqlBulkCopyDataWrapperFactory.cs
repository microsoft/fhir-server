// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public interface ISqlBulkCopyDataWrapperFactory
    {
        /// <summary>
        /// Create sql bulk copy wrapper, extract necessary information.
        /// </summary>
        /// <param name="resource">Import Resource</param>
        /// <returns>Bulk copy wrapper</returns>
        public SqlBulkCopyDataWrapper CreateSqlBulkCopyDataWrapper(ImportResource resource);

        /// <summary>
        /// Ensure the sql db initialized.
        /// </summary>
        public Task EnsureInitializedAsync();
    }
}
