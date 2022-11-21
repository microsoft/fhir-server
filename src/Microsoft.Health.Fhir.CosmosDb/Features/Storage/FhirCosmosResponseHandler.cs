// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirCosmosResponseHandler : RequestHandler
    {
        private const string _continuationTokenLimitHeaderName = "x-ms-documentdb-responsecontinuationtokenlimitinkb";
        private const string _consistencyLevelHeaderName = "x-ms-consistency-level";
        private const string _sessionTokenHeaderName = "x-ms-session-token";

        private static readonly string _validConsistencyLevelsForErrorMessage = string.Join(", ", Enum.GetNames(typeof(ConsistencyLevel)).Select(v => $"'{v}'"));
        private readonly Func<IScoped<Container>> _client;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly ICosmosResponseProcessor _cosmosResponseProcessor;

        public FhirCosmosResponseHandler(
            Func<IScoped<Container>> client,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            ICosmosResponseProcessor cosmosResponseProcessor)
        {
            _client = client;
            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _cosmosResponseProcessor = cosmosResponseProcessor;
        }

        public override async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            UpdateOptions(request);

            ResponseMessage response = await base.SendAsync(request, cancellationToken);

            await _cosmosResponseProcessor.ProcessResponse(response);

            if (!response.IsSuccessStatusCode)
            {
                await _cosmosResponseProcessor.ProcessErrorResponse(response);
            }

            return response;
        }

        private void UpdateOptions(RequestMessage options)
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;
            if (fhirRequestContext == null)
            {
                return;
            }

            if (fhirRequestContext.RequestHeaders.TryGetValue(CosmosDbHeaders.CosmosContinuationTokenSize, out var tokenSize))
            {
                var intTokenSize = int.TryParse(tokenSize, out var count) ? count : 0;
                if (intTokenSize != 0)
                {
                    if (intTokenSize < Constants.ContinuationTokenMinLimit || intTokenSize > Constants.ContinuationTokenMaxLimit)
                    {
                        throw new BadRequestException(string.Format(Resources.InvalidCosmosContinuationTokenSize, tokenSize));
                    }

                    options.Headers[_continuationTokenLimitHeaderName] = intTokenSize.ToString();
                }
                else
                {
                    throw new BadRequestException(string.Format(Resources.InvalidCosmosContinuationTokenSize, tokenSize));
                }
            }
            else
            {
                if (_cosmosDataStoreConfiguration.ContinuationTokenSizeLimitInKb != null)
                {
                    options.Headers[_continuationTokenLimitHeaderName] = _cosmosDataStoreConfiguration.ContinuationTokenSizeLimitInKb.ToString();
                }
            }

            (ConsistencyLevel? consistencyLevel, string sessionToken) = GetConsistencyHeaders();

            if (consistencyLevel == null && string.IsNullOrEmpty(sessionToken))
            {
                return;
            }

            if (consistencyLevel != null)
            {
                options.Headers[_consistencyLevelHeaderName] = consistencyLevel?.ToString();
            }

            if (!string.IsNullOrEmpty(sessionToken))
            {
                options.Headers[_sessionTokenHeaderName] = sessionToken;
            }
        }

        private (ConsistencyLevel? consistencyLevel, string sessionToken) GetConsistencyHeaders()
        {
            IFhirRequestContext fhirRequestContext = _fhirRequestContextAccessor.RequestContext;

            if (fhirRequestContext == null)
            {
                return (null, null);
            }

            ConsistencyLevel? requestedConsistencyLevel = null;

            if (fhirRequestContext.RequestHeaders.TryGetValue(CosmosDbHeaders.ConsistencyLevel, out var values))
            {
                if (!Enum.TryParse(values, out ConsistencyLevel parsedLevel))
                {
                    throw new BadRequestException(string.Format(CultureInfo.CurrentCulture, Resources.UnrecognizedConsistencyLevel, values, _validConsistencyLevelsForErrorMessage));
                }

                using var client = _client.Invoke();
                if (parsedLevel != client.Value.Database.Client.ClientOptions.ConsistencyLevel)
                {
                    if (!ValidateConsistencyLevel(parsedLevel))
                    {
                        throw new BadRequestException(string.Format(Resources.InvalidConsistencyLevel, parsedLevel, client.Value.Database.Client.ClientOptions.ConsistencyLevel));
                    }

                    requestedConsistencyLevel = parsedLevel;
                }
            }

            fhirRequestContext.RequestHeaders.TryGetValue(CosmosDbHeaders.SessionToken, out values);

            return (requestedConsistencyLevel, values);
        }

        /// <summary>
        /// Determines whether the requested consistency level is valid given the CosmosClient's consistency level.
        /// CosmosClient throws an ArgumentException when a requested consistency level is invalid. Since ArgumentException
        /// is not very specific and we would rather not inspect the exception message, we do the check ourselves here.
        /// Copied from the DocumentDB SDK.
        /// </summary>
        private bool ValidateConsistencyLevel(ConsistencyLevel desiredConsistency)
        {
            using var client = _client.Invoke();
            switch (client.Value.Database.Client.ClientOptions.ConsistencyLevel)
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
                    throw new NotSupportedException(nameof(client.Value.Database.Client.ClientOptions.ConsistencyLevel));
            }
        }
    }
}
