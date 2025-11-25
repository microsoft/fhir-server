// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    public static class AuthorizationServiceExtensions
    {
        /// <summary>
        /// Checks if a 'create' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'create' permission (SMART v2) or legacy 'write' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckCreateAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Write | (includeGranular ? DataActions.Create : DataActions.None);
            return service.CheckAccess(
                actions,
                x => (x & actions) != DataActions.None,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'delete' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="hardDelete">The value indicating whether it is soft or hard delete.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        public static Task<DataActions> CheckDeleteAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool hardDelete = false,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Delete | (hardDelete ? DataActions.HardDelete : DataActions.None);
            return service.CheckAccess(
                actions,
                x => x == actions,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'get' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'readById' permission (SMART v2) or legacy 'read' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckGetAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Read | (includeGranular ? DataActions.ReadById : DataActions.None);
            return service.CheckAccess(
                actions,
                x => (x & actions) != DataActions.None,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'patch' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'update' permission (SMART v2) or legacy 'write' + 'read' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckPatchAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Read | DataActions.Write;
            var granular = includeGranular ? DataActions.Update : DataActions.None;
            return service.CheckAccess(
                actions | granular,
                x => (x & actions) == actions || (x & granular) == granular,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'search' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>
        /// For SMART v2 compliance, search operations require the Search permission.
        /// SMART v2 scopes like "patient/Patient.r" allow read-only access without search capability,
        /// while "patient/Patient.s" or "patient/Patient.rs" include search permissions.
        /// Users with only read permission can access resources directly by ID but cannot search.
        /// We continue to allow DataActions.Read for legacy support.
        /// </remarks>
        public static Task<DataActions> CheckSearchAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Read | (includeGranular ? DataActions.Search : DataActions.None);
            return service.CheckAccess(
                actions,
                x => (x & actions) != DataActions.None,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'update' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'update' permission (SMART v2) or legacy 'write' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckUpdateAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Write | (includeGranular ? DataActions.Update : DataActions.None);
            return service.CheckAccess(
                actions,
                x => (x & actions) != DataActions.None,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'upsert' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'update' + 'create' permission (SMART v2) or legacy 'write' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckUpsertAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Write | (includeGranular ? DataActions.Create | DataActions.Update : DataActions.None);
            return service.CheckAccess(
                actions,
                x => (x & actions) != DataActions.None,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'conditionalCreate' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'create' + 'search' permission (SMART v2) or legacy 'write' + 'read' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckConditionalCreateAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Read | DataActions.Write;
            var gradular = includeGranular ? DataActions.Search | DataActions.Create : DataActions.None;
            return service.CheckAccess(
                actions | gradular,
                x => (x & actions) == actions || (x & gradular) == gradular,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'conditionalDelete' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="hardDelete">The value indicating whether it is soft or hard delete.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'search' + 'delete' permission (SMART v2) or legacy 'delete' + 'read' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckConditionalDeleteAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool hardDelete = false,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Delete | DataActions.Read | (hardDelete ? DataActions.HardDelete : DataActions.None);
            var gradular = includeGranular ? DataActions.Search : DataActions.None;
            return service.CheckAccess(
                actions | gradular,
                x => (x & actions) == actions || (x & gradular) == gradular,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'conditionalPatch' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'search' + 'update' permission (SMART v2) or legacy 'write' + 'read' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckConditionalPatchAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Read | DataActions.Write;
            var gradular = includeGranular ? DataActions.Search | DataActions.Update : DataActions.None;
            return service.CheckAccess(
                actions | gradular,
                x => (x & actions) == actions || (x & gradular) == gradular,
                throwException,
                cancellationToken);
        }

        /// <summary>
        /// Checks if a 'conditionalUpdate' access is permitted.
        /// </summary>
        /// <param name="service">The authorization service.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="includeGranular">The value indicating whether to include gradular permissions in a request.</param>
        /// <param name="throwException">The value indicating whether to throw an unauthorized exception when requested permissions aren't granted.</param>
        /// <returns>The task representing granted permissions.</returns>
        /// <remarks>The method checks for granular 'search' + 'update' permission (SMART v2) or legacy 'write' + 'read' permission (SMART v1/backward compatibility).</remarks>
        public static Task<DataActions> CheckConditionalUpdateAccess(
            this IAuthorizationService<DataActions> service,
            CancellationToken cancellationToken,
            bool includeGranular = true,
            bool throwException = true)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var actions = DataActions.Read | DataActions.Write;
            var gradular = includeGranular ? DataActions.Search | DataActions.Update : DataActions.None;
            return service.CheckAccess(
                actions | gradular,
                x => (x & actions) == actions || (x & gradular) == gradular,
                throwException,
                cancellationToken);
        }

        private static async Task<DataActions> CheckAccess(
            this IAuthorizationService<DataActions> service,
            DataActions actions,
            Func<DataActions, bool> verify,
            bool throwException,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(service, nameof(service));

            var granted = await service.CheckAccess(
                actions,
                cancellationToken);
            if (throwException && verify != null && !verify(granted))
            {
                throw new UnauthorizedFhirActionException();
            }

            return granted;
        }
    }
}
