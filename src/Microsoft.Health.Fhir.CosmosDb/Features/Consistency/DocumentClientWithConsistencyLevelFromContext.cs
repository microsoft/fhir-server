// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Consistency
{
    /// <summary>
    /// A <see cref="IDocumentClient"/> wrapper that sets the consistency level and, if applicable, the session token
    /// for each request.
    /// </summary>
    internal sealed partial class DocumentClientWithConsistencyLevelFromContext
    {
        private readonly IFhirContextAccessor _fhirContextAccessor;

        public DocumentClientWithConsistencyLevelFromContext(IDocumentClient inner, IFhirContextAccessor fhirContextAccessor)
            : this(inner)
        {
            EnsureArg.IsNotNull(fhirContextAccessor, nameof(fhirContextAccessor));
            _fhirContextAccessor = fhirContextAccessor;
        }

        private RequestOptions UpdateOptions(RequestOptions options)
        {
            var (consistencyLevel, sessionToken) = GetConsistencyHeaders();

            if (consistencyLevel == null && string.IsNullOrEmpty(sessionToken))
            {
                return options;
            }

            if (options == null)
            {
                options = new RequestOptions();
            }

            if (consistencyLevel != null)
            {
                options.ConsistencyLevel = consistencyLevel;
            }

            if (!string.IsNullOrEmpty(sessionToken))
            {
                options.SessionToken = sessionToken;
            }

            return options;
        }

        private FeedOptions UpdateOptions(FeedOptions options)
        {
            var (consistencyLevel, sessionToken) = GetConsistencyHeaders();

            if (consistencyLevel == null && string.IsNullOrEmpty(sessionToken))
            {
                return options;
            }

            if (options == null)
            {
                options = new FeedOptions();
            }

            if (consistencyLevel != null)
            {
                options.ConsistencyLevel = consistencyLevel;
            }

            if (!string.IsNullOrEmpty(sessionToken))
            {
                options.SessionToken = sessionToken;
            }

            return options;
        }

        private (ConsistencyLevel? consistencyLevel, string sessionToken) GetConsistencyHeaders()
        {
            IFhirContext fhirContext = _fhirContextAccessor.FhirContext;

            if (fhirContext == null)
            {
                return (null, null);
            }

            ConsistencyLevel? requestedConsistencyLevel = null;

            if (fhirContext.RequestHeaders.TryGetValue(CosmosDbConsistencyHeaders.ConsistencyLevel, out var values))
            {
                if (!Enum.TryParse(values, out ConsistencyLevel parsedLevel))
                {
                    throw new BadRequestException(string.Format(CultureInfo.CurrentCulture, Resources.UnrecognizedConsistencyLevel, values, string.Join(", ", Enum.GetNames(typeof(ConsistencyLevel)).Select(v => $"{v}"))));
                }

                if (parsedLevel != _inner.ConsistencyLevel)
                {
                    if (!ValidateConsistencyLevel(parsedLevel))
                    {
                        throw new BadRequestException(string.Format(Resources.InvalidConsistencyLevel, parsedLevel, _inner.ConsistencyLevel));
                    }

                    requestedConsistencyLevel = parsedLevel;
                }
            }

            fhirContext.RequestHeaders.TryGetValue(CosmosDbConsistencyHeaders.SessionToken, out values);

            return (requestedConsistencyLevel, values);
        }

        /// <summary>
        /// Determines whether the requested consistency level is valid given the DocumentClient's consistency level.
        /// DocumentClient throws an ArgumentException when a requested consistency level is invalid. Since ArgumentException
        /// is not very specific and we would rather not inspect the exception message, we do the check ourselves here.
        /// Copied from the DocumentDB SDK.
        /// </summary>
        private bool ValidateConsistencyLevel(ConsistencyLevel desiredConsistency)
        {
            switch (_inner.ConsistencyLevel)
            {
                case ConsistencyLevel.Strong:
                    return desiredConsistency == ConsistencyLevel.Strong || desiredConsistency == ConsistencyLevel.BoundedStaleness || desiredConsistency == ConsistencyLevel.Session || desiredConsistency == ConsistencyLevel.Eventual || desiredConsistency == ConsistencyLevel.ConsistentPrefix;
                case ConsistencyLevel.BoundedStaleness:
                    return desiredConsistency == ConsistencyLevel.BoundedStaleness || desiredConsistency == ConsistencyLevel.Session || desiredConsistency == ConsistencyLevel.Eventual || desiredConsistency == ConsistencyLevel.ConsistentPrefix;
                case ConsistencyLevel.Session:
                case ConsistencyLevel.Eventual:
                case ConsistencyLevel.ConsistentPrefix:
                    return desiredConsistency == ConsistencyLevel.Session || desiredConsistency == ConsistencyLevel.Eventual || desiredConsistency == ConsistencyLevel.ConsistentPrefix;
                default:
                    throw new NotSupportedException(nameof(_inner.ConsistencyLevel));
            }
        }

        private T ProcessResponse<T>(T response)
            where T : ResourceResponseBase
        {
            AddSessionTokenToResponseHeaders(response.SessionToken);
            return response;
        }

        private FeedResponse<T> ProcessResponse<T>(FeedResponse<T> response)
        {
            AddSessionTokenToResponseHeaders(response.SessionToken);
            return response;
        }

        private StoredProcedureResponse<T> ProcessResponse<T>(StoredProcedureResponse<T> response)
        {
            AddSessionTokenToResponseHeaders(response.SessionToken);
            return response;
        }

        private void AddSessionTokenToResponseHeaders(string sessionToken)
        {
            if (!string.IsNullOrEmpty(sessionToken))
            {
                IFhirContext fhirContext = _fhirContextAccessor.FhirContext;
                if (fhirContext != null)
                {
                    fhirContext.ResponseHeaders[CosmosDbConsistencyHeaders.SessionToken] = sessionToken;
                }
            }
        }

        Task<StoredProcedureResponse<TValue>> IDocumentClient.ExecuteStoredProcedureAsync<TValue>(string storedProcedureLink, params object[] procedureParams)
        {
            return ((IDocumentClient)this).ExecuteStoredProcedureAsync<TValue>(storedProcedureLink, UpdateOptions(default(RequestOptions)), procedureParams);
        }

        Task<StoredProcedureResponse<TValue>> IDocumentClient.ExecuteStoredProcedureAsync<TValue>(Uri storedProcedureUri, params dynamic[] procedureParams)
        {
            return ((IDocumentClient)this).ExecuteStoredProcedureAsync<TValue>(storedProcedureUri, UpdateOptions(default(RequestOptions)), procedureParams);
        }
    }
}
