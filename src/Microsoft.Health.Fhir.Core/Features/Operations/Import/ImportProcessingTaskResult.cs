// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportProcessingTaskResult
    {
        public string ResourceType { get; set; }

        public long SucceedCount { get; set; }

        public long FailedCount { get; set; }

        public string ErrorLogLocation { get; set; }

        public string ImportError { get; set; }
    }
}
