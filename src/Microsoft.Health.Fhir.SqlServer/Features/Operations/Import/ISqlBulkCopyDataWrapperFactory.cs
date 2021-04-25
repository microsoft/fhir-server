// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public interface ISqlBulkCopyDataWrapperFactory
    {
        public SqlBulkCopyDataWrapper CreateSqlBulkCopyDataWrapper(ImportResource resource);
    }
}
