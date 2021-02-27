// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Azure;
using EnsureThat;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public static class StorageExceptionParser
    {
        /// <summary>
        /// Attempts to return a HttpStatusCode that represents the information present in the
        /// <see cref="StorageException"/> object. If there is no information, returns a <see cref="HttpStatusCode.InternalServerError"/>.
        /// </summary>
        /// <param name="storageException"><see cref="StorageException"/> object that needs to be parsed.</param>
        /// <returns>A corresponding <see cref="HttpStatusCode"/></returns>
        public static HttpStatusCode ParseStorageException(RequestFailedException storageException)
        {
            EnsureArg.IsNotNull(storageException, nameof(storageException));

            // Let's check whether there is a valid http status code that we can return.
            if (Enum.IsDefined(typeof(HttpStatusCode), storageException.Status))
            {
                return (HttpStatusCode)storageException.Status;
            }

            // We don't have a valid status code. Let's look at the message to try to get more information.
            if (storageException.Message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase))
            {
                return HttpStatusCode.BadRequest;
            }

            return HttpStatusCode.InternalServerError;
        }
    }
}
