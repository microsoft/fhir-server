// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportTaskResult
    {
        public string Request { get; set; }

        public IReadOnlyCollection<ImportOperationOutcome> Output { get; set; }

        public IReadOnlyCollection<ImportOperationOutcome> Error { get; set; }
    }
}
