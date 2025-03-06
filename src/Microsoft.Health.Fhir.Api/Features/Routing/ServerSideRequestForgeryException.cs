// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Routing
{
    /// <summary>
    /// Represents an exception that is thrown when a user engages in behavior that would make others
    /// suspectible to a Server-Side Request Forgery (SSRF) attack.
    /// </summary>
    public sealed class ServerSideRequestForgeryException : FhirException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerSideRequestForgeryException"/> class
        /// with the given message.
        /// </summary>
        /// <param name="message">An exception message.</param>
        public ServerSideRequestForgeryException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServerSideRequestForgeryException"/> class
        /// with the given message and inner exception.
        /// </summary>
        /// <param name="message">An exception message.</param>
        /// <param name="innerException">An optionl inner exception.</param>
        public ServerSideRequestForgeryException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
