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
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// A <see cref="IDocumentClient"/> wrapper that, for each request:
    /// (1) sets the consistency level and, if applicable, the session token
    /// (2) Sets <see cref="FeedOptions.ResponseContinuationTokenLimitInKb"/>
    /// (3) Sets the <see cref="CosmosDbHeaders.RequestCharge"/> response header.
    /// (4) In the event of a 429 response from the database, throws a <see cref="RequestRateTooLargeException"/>.
    /// </summary>
    internal sealed partial class FhirDocumentClient
    {
        private static readonly string ValidConsistencyLevelsForErrorMessage = string.Join(", ", Enum.GetNames(typeof(ConsistencyLevel)).Select(v => $"'{v}'"));
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly int? _continuationTokenSizeLimitInKb;

        public FhirDocumentClient(IDocumentClient inner, IFhirRequestContextAccessor fhirRequestContextAccessor, int? continuationTokenSizeLimitInKb)
            : this(inner)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _continuationTokenSizeLimitInKb = continuationTokenSizeLimitInKb;
        }

        private RequestOptions UpdateOptions(RequestOptions options)
        {
            (ConsistencyLevel? consistencyLevel, string sessionToken) = GetConsistencyHeaders();

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
            (ConsistencyLevel? consistencyLevel, string sessionToken) = GetConsistencyHeaders();

            if (_continuationTokenSizeLimitInKb == null && consistencyLevel == null && string.IsNullOrEmpty(sessionToken))
            {
                return options;
            }

            if (options == null)
            {
                options = new FeedOptions();
            }

            if (_continuationTokenSizeLimitInKb != null)
            {
                options.ResponseContinuationTokenLimitInKb = _continuationTokenSizeLimitInKb;
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
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.FhirRequestContext;

            if (fhirRequestContext == null)
            {
                return (null, null);
            }

            ConsistencyLevel? requestedConsistencyLevel = null;

            if (fhirRequestContext.RequestHeaders.TryGetValue(CosmosDbHeaders.ConsistencyLevel, out var values))
            {
                if (!Enum.TryParse(values, out ConsistencyLevel parsedLevel))
                {
                    throw new BadRequestException(string.Format(CultureInfo.CurrentCulture, Resources.UnrecognizedConsistencyLevel, values, ValidConsistencyLevelsForErrorMessage));
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

            fhirRequestContext.RequestHeaders.TryGetValue(CosmosDbHeaders.SessionToken, out values);

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
                    throw new NotSupportedException(nameof(IDocumentClient.ConsistencyLevel));
            }
        }

        private T ProcessResponse<T>(T response)
            where T : ResourceResponseBase
        {
            _fhirRequestContextAccessor.FhirRequestContext.UpdateResponseHeaders(response.SessionToken, response.RequestCharge);
            return response;
        }

        private FeedResponse<T> ProcessResponse<T>(FeedResponse<T> response)
        {
            _fhirRequestContextAccessor.FhirRequestContext.UpdateResponseHeaders(response.SessionToken, response.RequestCharge);
            return response;
        }

        private StoredProcedureResponse<T> ProcessResponse<T>(StoredProcedureResponse<T> response)
        {
            _fhirRequestContextAccessor.FhirRequestContext.UpdateResponseHeaders(response.SessionToken, response.RequestCharge);
            return response;
        }

        private void ProcessException(Exception ex)
        {
            _fhirRequestContextAccessor.FhirRequestContext.ProcessException(ex);
        }

        Task<StoredProcedureResponse<TValue>> IDocumentClient.ExecuteStoredProcedureAsync<TValue>(string storedProcedureLink, params object[] procedureParams)
        {
            return ((IDocumentClient)this).ExecuteStoredProcedureAsync<TValue>(storedProcedureLink, default, procedureParams);
        }

        Task<StoredProcedureResponse<TValue>> IDocumentClient.ExecuteStoredProcedureAsync<TValue>(Uri storedProcedureUri, params dynamic[] procedureParams)
        {
            return ((IDocumentClient)this).ExecuteStoredProcedureAsync<TValue>(storedProcedureUri, default, procedureParams);
        }
    }
}
