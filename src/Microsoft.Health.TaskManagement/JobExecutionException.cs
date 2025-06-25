// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.JobManagement
{
    public class JobExecutionException : Exception
    {
        public JobExecutionException(string message, bool isCustomerCaused)
            : base(message)
        {
            Error = null;
            IsCustomerCaused = isCustomerCaused;
        }

        public JobExecutionException(string message, object error, bool isCustomerCaused)
            : base(message)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            Error = error;
            IsCustomerCaused = isCustomerCaused;
        }

        public JobExecutionException(string message, Exception innerException, bool isCustomerCaused)
            : this(message, null, innerException, isCustomerCaused)
        {
        }

        public JobExecutionException(string message, object error, Exception innerException, bool isCustomerCaused)
            : base(message, innerException)
        {
            EnsureArg.IsNotNull(message, nameof(message));
            EnsureArg.IsNotNull(innerException, nameof(innerException));

            Error = error;
            IsCustomerCaused = isCustomerCaused;
        }

        public object Error { get; private set; }

        public bool IsCustomerCaused { get; private set; }
    }
}
