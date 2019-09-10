// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    /// <summary>
    /// Provides the ability to lookup audit event type.
    /// </summary>
    public interface IAuditEventTypeMapping
    {
        /// <summary>
        /// Gets the audit event type from the <paramref name="controllerName"/> and <paramref name="actionName"/>.
        /// </summary>
        /// <param name="controllerName">The controller name.</param>
        /// <param name="actionName">The action name.</param>
        /// <returns>The audit event type if exists; <c>null</c> if anonymous access is allowed.</returns>
        /// <exception cref="AuditEventTypeMapping">Thrown when there is no audit event type associated with the <paramref name="controllerName"/> and <paramref name="actionName"/>.</exception>
        string GetAuditEventType(string controllerName, string actionName);
    }
}
