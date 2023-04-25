// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public interface ISqlBulkCopyDataWrapperFactory
    {
        /// <summary>
        /// Ensure the sql db initialized.
        /// </summary>
        public Task EnsureInitializedAsync();
    }
}
