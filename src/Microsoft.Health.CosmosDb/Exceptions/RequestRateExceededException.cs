// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.CosmosDb.Exceptions
{
    /// <summary>
    /// An exception indicating that the request rate has exceeded the maximum API request rate.
    /// </summary>
    public class RequestRateExceededException : CosmosDbException
    {
        public RequestRateExceededException()
        {
            CustomExceptionMessage = "request rate exceeded";
        }
    }
}
