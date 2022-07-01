// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal static class ImportResourceSqlExtentions
    {
        internal static BulkImportResourceTypeV1Row ExtractBulkImportResourceTypeV1Row(this ImportResource importResource, short resourceTypeId)
        {
            return new BulkImportResourceTypeV1Row(resourceTypeId, importResource.Resource.ResourceId, 1, false, importResource.Id, false, "POST", importResource.CompressedStream, true, importResource.Resource.SearchParameterHash);
        }

        internal static bool ContainsError(this ImportResource importResource)
        {
            return !string.IsNullOrEmpty(importResource.ImportError);
        }
    }
}
