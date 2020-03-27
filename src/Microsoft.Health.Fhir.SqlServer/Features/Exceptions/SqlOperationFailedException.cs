// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Exceptions
{
    public class SqlOperationFailedException : Exception
    {
        public SqlOperationFailedException(string message)
            : base(message)
        {
            EnsureArg.IsNotNullOrWhiteSpace(message, nameof(message));
        }
    }
}
