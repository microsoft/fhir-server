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
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Queries;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirCosmosReponseHandler : RequestHandler
    {
        private static readonly string ValidConsistencyLevelsForErrorMessage = string.Join(", ", Enum.GetNames(typeof(ConsistencyLevel)).Select(v => $"'{v}'"));
        private readonly Func<IScoped<Container>> _client;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ICosmosResponseProcessor _cosmosResponseProcessor;

        public FhirCosmosReponseHandler(
            Func<IScoped<Container>> client,
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
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
            ResponseMessage response = null;

            response = await base.SendAsync(request, cancellationToken);
            await _cosmosResponseProcessor.ProcessResponse(response.Headers.Session, response.Headers.RequestCharge, response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                await _cosmosResponseProcessor.ProcessException(response);
            }

            return response;
        }

        private void UpdateOptions(RequestMessage options)
        {
            if (_cosmosDataStoreConfiguration.InitialDatabaseThroughput != null)
            {
                options.Headers["x-ms-documentdb-responsecontinuationtokenlimitinkb"] = _cosmosDataStoreConfiguration.ContinuationTokenSizeLimitInKb?.ToString();
            }

            (ConsistencyLevel? consistencyLevel, string sessionToken) = GetConsistencyHeaders();

            if (consistencyLevel == null && string.IsNullOrEmpty(sessionToken))
            {
                return;
            }

            if (consistencyLevel != null)
            {
                options.Headers["x-ms-consistency-level"] = consistencyLevel?.ToString();
            }

            if (!string.IsNullOrEmpty(sessionToken))
            {
                options.Headers["x-ms-session-token"] = sessionToken;
            }
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
        /// Determines whether the requested consistency level is valid given the DocumentClient's consistency level.
        /// DocumentClient throws an ArgumentException when a requested consistency level is invalid. Since ArgumentException
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
