// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.CosmosDb.Exceptions
{
    public class CosmosDbException : Exception
    {
        public string CustomExceptionMessage { get; protected set; }
    }
}
