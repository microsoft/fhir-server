// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Net;
using Microsoft.Azure.Documents;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public static class DocumentClientExceptionExtensions
    {
        /// <summary>
        /// When a javascript stored procedure throws an exception in code, such as
        /// throw new Error(404, "Document not found");
        /// the status code of the exception will be 400 bad request, but there will be a substatus header
        /// with the error code specified in the Error object (404 in this example).
        /// This methods returns the substatus code if it is present.
        /// </summary>
        /// <param name="e">The exception object</param>
        /// <returns>The status code of null if not present (or not an integer)</returns>
        public static HttpStatusCode? GetSubStatusCode(this DocumentClientException e)
        {
            return int.TryParse(e.ResponseHeaders.Get(CosmosDbHeaders.SubStatus), NumberStyles.Integer, CultureInfo.InvariantCulture, out int subStatusCode)
                ? (HttpStatusCode?)subStatusCode
                : null;
        }
    }
}
