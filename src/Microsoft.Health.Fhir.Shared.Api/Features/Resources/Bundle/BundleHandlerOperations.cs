// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.ElementModel.ScopedNode;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Set of static methods used as part of the bundle handling logic.
    /// </summary>
    public static class BundleHandlerOperations
    {
        /// <summary>
        /// Delay logic used in case of retry operations.
        /// </summary>
        public static async Task DelayWithRetryAfterAsync(HttpContext httpContext, CancellationToken cancellationToken)
        {
            int retryDelay = 2;

            var retryAfterValues = httpContext.Response.Headers.GetCommaSeparatedValues("Retry-After");
            if (retryAfterValues != StringValues.Empty && int.TryParse(retryAfterValues[0], out var retryHeaderValue))
            {
                if (retryHeaderValue > 0 && retryHeaderValue <= 15)
                {
                    retryDelay = retryHeaderValue;
                }
            }

            await Task.Delay(retryDelay * 1000, cancellationToken); // multiply by 1000 as retry-header specifies delay in seconds
        }

        /// <summary>
        /// Given a list of exceptions raised during a bundle execution, prioritizes the exceptions of type <see cref="FhirTransactionFailedException"/>, based on their status code, to determine which exception should be returned to the customer.
        /// </summary>
        public static FhirTransactionFailedException GetPrioritizedClientException(Exception exception)
        {
            if (exception == null)
            {
                return null;
            }

            // Scenario 1 - When the exception is an AggregateException and there are inner exceptions to be analized.
            if (exception is AggregateException aggregateException && aggregateException.InnerExceptions != null && aggregateException.InnerExceptions.Any())
            {
                IEnumerable<FhirTransactionFailedException> failedTransactionExceptions = aggregateException.Flatten().InnerExceptions.OfType<FhirTransactionFailedException>().ToList();

                if (failedTransactionExceptions.Any())
                {
                    // Ensure that, if a transaction fails with a client error, then the exception with the customer error is prioritized.
                    FhirTransactionFailedException customerException = failedTransactionExceptions
                        .FirstOrDefault(e =>
                            e.ResponseStatusCode != HttpStatusCode.FailedDependency &&
                            e.ResponseStatusCode != HttpStatusCode.RequestTimeout);

                    if (customerException != null)
                    {
                        return customerException;
                    }

                    // At this point, prioritize other types of client errors.
                    return failedTransactionExceptions.FirstOrDefault();
                }
            }

            // Scenario 2 - When the exception is not an AggregateException.
            if (exception is FhirTransactionFailedException transactionFailedException)
            {
                return transactionFailedException;
            }

            return null;
        }

        /// <summary>
        /// Returns the Bundle Processing Logic to be used for the current request, based on the presence of the Bundle-Processing-Logic header and the validity of its value.
        /// </summary>
        public static BundleProcessingLogic GetBundleProcessingLogic(BundleConfiguration bundleConfiguration, HttpContext outerHttpContext, BundleType? bundleType)
        {
            EnsureArg.IsNotNull(outerHttpContext, nameof(outerHttpContext));

            if (bundleType.HasValue)
            {
                if (bundleType.Value == BundleType.Transaction)
                {
                    // For transactions, the default processing logic is parallel.
                    return outerHttpContext.GetBundleProcessingLogic(bundleConfiguration.TransactionDefaultProcessingLogic);
                }
                else if (bundleType.Value == BundleType.Batch)
                {
                    // For batch, the default processing logic is parallel.
                    return outerHttpContext.GetBundleProcessingLogic(bundleConfiguration.BatchDefaultProcessingLogic);
                }
            }

            // Reaching this part of the code means that the bundle type is not set or it's using an invalid value.
            // Returning sequential as the default processing logic for both cases.
            return BundleProcessingLogic.Sequential;
        }

        public static bool ContainsSearchParams(Hl7.Fhir.Model.Bundle bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            return bundle.Entry.Any(e => e.Resource?.TypeName == KnownResourceTypes.SearchParameter ||
                    e.Request?.Url?.StartsWith(KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase) == true); // for deletes type name is not populated, so checking url
        }

        /// <summary>
        /// Checks for duplicate search parameter codes and urls in the bundle and throws a <see cref="RequestNotValidException"/> if any duplicates are found.
        /// </summary>
        /// <returns>Returns true if there are search parameters in the bundle; otherwise, false.</returns>
        /// <remarks>If any duplicate search parameter codes or urls are found, a <see cref="RequestNotValidException"/> is thrown.</remarks>
        public static bool CheckSearchParamInputAndPossibleConflicts(Hl7.Fhir.Model.Bundle bundle, IModelInfoProvider modelInfoProvider)
        {
            var codes = new HashSet<(string Type, string Code)>();
            var urls = new HashSet<string>();
            var dupCodes = new HashSet<(string Type, string Code)>();
            var dupUrls = new HashSet<string>();
            var searchParamsInBundle = false;
            foreach (var param in bundle.Entry.Select(_ => _.Resource as SearchParameter).Where(_ => _ != null))
            {
                if (param.Code != null && param.Base != null)
                {
                    var allResourceTypes = SearchParameterDefinitionManager.GetDerivedResourceTypes(modelInfoProvider, param.Base.Where(_ => _ != null).Select(_ => _.Value.ToString()).ToList());
                    foreach (var resourceType in allResourceTypes.Where(_ => !codes.Add((_, param.Code))))
                    {
                        dupCodes.Add((resourceType, param.Code));
                    }
                }

                if (param.Url != null && !urls.Add(param.Url))
                {
                    dupUrls.Add(param.Url);
                }

                searchParamsInBundle = true;
            }

            if (dupCodes.Count > 0 || dupUrls.Count > 0)
            {
                if (dupCodes.Count == 0)
                {
                    throw new RequestNotValidException(string.Format(Api.Resources.DuplicateSearchParamUrlsInBundle, string.Join(", ", dupUrls)));
                }
                else if (dupUrls.Count == 0)
                {
                    throw new RequestNotValidException(string.Format(Api.Resources.DuplicateSearchParamCodesInBundle, string.Join(", ", dupCodes)));
                }

                throw new RequestNotValidException(string.Format(Api.Resources.DuplicateSearchParamCodesAndUrlsInBundle, string.Join(", ", dupCodes), string.Join(", ", dupUrls)));
            }

            // for deletes Entry.Resource is null. need to check in other way
            if (!searchParamsInBundle && bundle.Entry.Any(e => e.Request.Method == HTTPVerb.DELETE && e.Request.Url.StartsWith(KnownResourceTypes.SearchParameter, StringComparison.OrdinalIgnoreCase)))
            {
                searchParamsInBundle = true;
            }

            return searchParamsInBundle;
        }

        /// <summary>
        /// Determines whether a bundle has been cancelled by the client.
        /// If the cancellation is requested and the elapsed time is less than the max bundle execution time, it is assumed that the client cancelled the request.
        /// </summary>
        public static bool HasCancellationHappenedBeforeMaxExecutionTime(TimeSpan elapsedTime, BundleConfiguration bundleConfiguration, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(bundleConfiguration, nameof(bundleConfiguration));

            return cancellationToken.IsCancellationRequested && elapsedTime.TotalSeconds < bundleConfiguration.MaxExecutionTimeInSeconds;
        }

        internal static bool IsBundleProcessingLogicValid(HttpContext outerHttpContext)
        {
            EnsureArg.IsNotNull(outerHttpContext, nameof(outerHttpContext));

            return outerHttpContext.IsBundleProcessingLogicValid();
        }
    }
}
