// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using EnsureThat;

namespace FhirSchemaManager.Model
{
    public class ErrorDescription
    {
        public ErrorDescription(int statusCode, string message)
        {
            EnsureArg.IsNotNull<int>(statusCode, nameof(statusCode));
            EnsureArg.IsNotNull(message, nameof(message));

            StatusCode = statusCode;
            Message = message;
        }

        public int StatusCode { get; }

        public string Message { get; }
    }
}
