// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.TaskManagement
{
    public class TaskConflictException : Exception
    {
        public TaskConflictException(string message)
            : base(message)
        {
            EnsureArg.IsNotNullOrEmpty(message, nameof(message));
        }

        public TaskConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
            EnsureArg.IsNotNullOrEmpty(message, nameof(message));
            EnsureArg.IsNotNull(innerException, nameof(innerException));
        }
    }
}
