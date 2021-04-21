// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.TaskManagement
{
    public class TaskAlreadyCompletedException : Exception
    {
        public TaskAlreadyCompletedException(string message)
            : base(message)
        {
        }

        public TaskAlreadyCompletedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
