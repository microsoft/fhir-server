// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Import;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal static class ImportResourceSqlExtentions
    {
        internal static bool ContainsError(this ImportResource importResource)
        {
            return !string.IsNullOrEmpty(importResource.ImportError);
        }
    }
}
