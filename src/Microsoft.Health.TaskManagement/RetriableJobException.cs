// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime;
using EnsureThat;

namespace Microsoft.Health.JobManagement
{
#pragma warning disable CS0618 // Type or member is obsolete. We should remove this from the code.
    public class RetriableJobException : Exception
    {
        public RetriableJobException(string message)
            : base(message)
        {
            EnsureArg.IsNotNull(message, nameof(message));
        }

        public RetriableJobException(string message, Exception innerException)
            : base(message, innerException)
        {
            EnsureArg.IsNotNull(message, nameof(message));
            EnsureArg.IsNotNull(innerException, nameof(innerException));
        }
    }
#pragma warning restore CS0618 // Type or member is obsolete
}
