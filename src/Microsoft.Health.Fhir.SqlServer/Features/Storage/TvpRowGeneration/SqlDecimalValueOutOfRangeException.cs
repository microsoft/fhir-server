// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal sealed class SqlDecimalValueOutOfRangeException : ArgumentOutOfRangeException
    {
        public SqlDecimalValueOutOfRangeException(string paramName, string message)
            : base(paramName, message)
        {
        }
    }
}
