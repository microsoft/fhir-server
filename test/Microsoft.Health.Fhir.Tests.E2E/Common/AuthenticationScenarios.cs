// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    public enum AuthenticationScenarios
    {
        /// <summary>
        /// Represents a scenario when no auth(no username or password) is provided.
        /// </summary>
        NOAUTH,

        /// <summary>
        /// Represents a scenario when an invalid auth (wrong username/password or expired token) is provided.
        /// </summary>
        INVALIDAUTH,

        /// <summary>
        /// Represents a scenario when a client with wrong audience is provided
        /// </summary>
        AUTHWITHWRONGAUDIENCE,

        /// <summary>
        /// Represents a scenario when an valid auth is provided
        /// </summary>
        VALIDAUTH,
    }
}
