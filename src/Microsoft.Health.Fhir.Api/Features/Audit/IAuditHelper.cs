// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides helper methods for auditing.
    /// </summary>
    public interface IAuditHelper
    {
        /// <summary>
        /// Gets the audit event type from the <paramref name="controllerName"/> and <paramref name="actionName"/>.
        /// </summary>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="actionName">The action name.</param>
        /// <returns>The audit event type if exists; <c>null</c> if anonymous access is allowed.</returns>
        /// <exception cref="AuditException">Thrown when the audit event type could not be found.</exception>
        string GetAuditEventType(string controllerName, string actionName);

        /// <summary>
        /// Logs an audit entry for executing the <paramref name="controllerName"/> and <paramref name="actionName"/>.
        /// </summary>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="actionName">The action name.</param>
        /// <param name="httpContext">The HTTP context.</param>
        /// <param name="claimsExtractor">The extractor used to extract claims.</param>
        void LogExecuting(string controllerName, string actionName, HttpContext httpContext, IClaimsExtractor claimsExtractor);

        /// <summary>
        /// Logs an audit entry for executed the <paramref name="controllerName"/> and <paramref name="actionName"/>.
        /// </summary>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="actionName">The action name.</param>
        /// <param name="responseResultType">The optional response result type.</param>
        /// <param name="httpContext">The HTTP context.</param>
        /// <param name="claimsExtractor">The extractor used to extract claims.</param>
        void LogExecuted(string controllerName, string actionName, string responseResultType, HttpContext httpContext, IClaimsExtractor claimsExtractor);
    }
}
