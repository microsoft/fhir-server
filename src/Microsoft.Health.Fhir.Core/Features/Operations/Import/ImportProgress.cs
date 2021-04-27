// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProgress
    {
        public long SucceedImportCount { get; set; }

        public long FailedImportCount { get; set; }

        public long CurrentIndex { get; set; }
    }
}
