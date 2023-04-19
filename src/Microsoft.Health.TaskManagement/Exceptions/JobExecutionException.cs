// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.TaskManagement.Exceptions
{
    public class JobExecutionException : Exception
    {
        public JobExecutionException(string message)
            : base(message)
        {
            Error = null;
        }

        public JobExecutionException(string message, object error)
            : base(message)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            Error = error;
        }

        public JobExecutionException(string message, Exception innerException)
            : this(message, null, innerException)
        {
        }

        public JobExecutionException(string message, object error, Exception innerException)
            : base(message, innerException)
        {
            EnsureArg.IsNotNull(message, nameof(message));
            EnsureArg.IsNotNull(innerException, nameof(innerException));

            Error = error;
        }

        public object Error { get; private set; }

        public bool RequestCancellationOnFailure { get; set; }
    }
}
