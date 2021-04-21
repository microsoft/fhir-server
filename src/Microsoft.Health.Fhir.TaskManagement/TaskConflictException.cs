// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.TaskManagement
{
    public class TaskConflictException : Exception
    {
        public TaskConflictException(string message)
            : base(message)
        {
        }

        public TaskConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
