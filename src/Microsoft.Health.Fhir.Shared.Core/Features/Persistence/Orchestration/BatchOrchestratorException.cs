// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BatchOrchestratorException : Exception
    {
        public BatchOrchestratorException(string message)
            : base(message)
        {
        }

        public BatchOrchestratorException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
