// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    /// <summary>
    /// Exception for reindex processing job soft failures that should not cancel other jobs in the same group.
    /// This allows individual processing jobs to fail while the orchestrator continues processing remaining jobs.
    /// </summary>
    public class ReindexProcessingJobSoftException : JobExecutionSoftFailureException
    {
        public ReindexProcessingJobSoftException(string message)
            : base(message, isCustomerCaused: false)
        {
        }

        public ReindexProcessingJobSoftException(string message, bool isCustomerCaused)
            : base(message, isCustomerCaused)
        {
        }

        public ReindexProcessingJobSoftException(string message, Exception innerException)
            : base(message, innerException, isCustomerCaused: false)
        {
        }

        public ReindexProcessingJobSoftException(string message, Exception innerException, bool isCustomerCaused)
            : base(message, innerException, isCustomerCaused)
        {
        }

        public ReindexProcessingJobSoftException(string message, object error, bool isCustomerCaused)
            : base(message, error, isCustomerCaused)
        {
        }

        public ReindexProcessingJobSoftException(string message, object error, Exception innerException, bool isCustomerCaused)
            : base(message, error, innerException, isCustomerCaused)
        {
        }
    }
}
