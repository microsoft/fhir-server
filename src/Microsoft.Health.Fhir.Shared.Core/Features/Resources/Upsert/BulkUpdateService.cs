// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid.Tags;
using DotLiquid.Util;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Patch;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.JobManagement;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using SharpCompress.Common;
using static Hl7.Fhir.Model.Parameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class BulkUpdateService : IBulkUpdateService
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Lazy<IConformanceProvider> _conformanceProvider;
        private readonly IScopeProvider<IFhirDataStore> _fhirDataStoreFactory;
        private readonly IScopeProvider<ISearchService> _searchServiceFactory;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly FhirRequestContextAccessor _contextAccessor;
        private readonly IAuditLogger _auditLogger;
        private readonly CoreFeatureConfiguration _configuration;
        private readonly ILogger<BulkUpdateService> _logger;
        private readonly HashSet<string> _excludedResourceTypes = ["SearchParameter", "StructureDefinition"];

        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";

        public BulkUpdateService(
            IResourceWrapperFactory resourceWrapperFactory,
            Lazy<IConformanceProvider> conformanceProvider,
            IScopeProvider<IFhirDataStore> fhirDataStoreFactory,
            IScopeProvider<ISearchService> searchServiceFactory,
            ResourceIdProvider resourceIdProvider,
            FhirRequestContextAccessor contextAccessor,
            IAuditLogger auditLogger,
            IOptions<CoreFeatureConfiguration> configuration,
            ILogger<BulkUpdateService> logger)
        {
            _resourceWrapperFactory = EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            _conformanceProvider = EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            _fhirDataStoreFactory = EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            _searchServiceFactory = EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            _resourceIdProvider = EnsureArg.IsNotNull(resourceIdProvider, nameof(resourceIdProvider));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _auditLogger = EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _configuration = EnsureArg.IsNotNull(configuration.Value, nameof(configuration));
        }

        public async Task<BulkUpdateResult> UpdateMultipleAsync(string resourceType, string fhirPatchParameters, int parallelThreads, bool readNextPage, uint maximumNumberOfResourcesPerQuery, bool isIncludesRequest, IReadOnlyList<Tuple<string, string>> conditionalParameters, BundleResourceContext bundleResourceContext, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<SearchResultEntry> searchResults;
            bool tooManyIncludeResults = false;
            SearchResult searchResult;
            string ct;
            string ict;

            searchResult = await Search(resourceType, isIncludesRequest, conditionalParameters, cancellationToken);

            Dictionary<string, long> resourceTypesUpdated = new Dictionary<string, long>();
            BulkUpdateResult finalBulkUpdateResult = new BulkUpdateResult();
            Dictionary<string, long> totalResources = new Dictionary<string, long>();
            Dictionary<string, long> resourcesIgnored = new Dictionary<string, long>();
            Dictionary<string, long> commonPatchFailures = new Dictionary<string, long>();
            Dictionary<string, string> commonPatchFailureReasons = new Dictionary<string, string>();
            ConcurrentDictionary<string, long> patchFailures = new ConcurrentDictionary<string, long>();
            Dictionary<string, ConditionalPatchResourceRequest> conditionalPatchResourceRequests = new Dictionary<string, ConditionalPatchResourceRequest>();
            ConcurrentDictionary<string, List<(string, Exception)>> patchExceptions = new ConcurrentDictionary<string, List<(string, Exception)>>();
            var updateTasks = new List<Task<Dictionary<string, long>>>();
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Deserialize the FHIR patch parameters from the payload
            var customFhirJsonSerializer = new CustomFhirJsonSerializer<Hl7.Fhir.Model.Parameters>();
            var deserializedFhirPatchParameters = customFhirJsonSerializer.Deserialize(fhirPatchParameters);
            bool pageOne = true;

            try
            {
                ct = isIncludesRequest ? searchResult.IncludesContinuationToken : searchResult.ContinuationToken;
                while ((searchResult.Results != null && searchResult.Results.Any()) || !string.IsNullOrEmpty(ct))
                {
                    ct = isIncludesRequest ? searchResult.IncludesContinuationToken : searchResult.ContinuationToken;
                    ict = searchResult.IncludesContinuationToken;
                    searchResults = searchResult.Results.ToList();

                    // Group the results based on the resource type and prepare the conditional patch requests
                    BuildConditionalPatchRequests(conditionalParameters, bundleResourceContext, searchResults, totalResources, resourcesIgnored, commonPatchFailures, conditionalPatchResourceRequests, deserializedFhirPatchParameters);

                    // Filter out the seachResults which are not in resourcesIgnored and commonPatchFailures
                    searchResults = searchResults.Where(result => !resourcesIgnored.ContainsKey(result.Resource.ResourceTypeName) && !commonPatchFailures.ContainsKey(result.Resource.ResourceTypeName)).ToList();

                    // Apply the patch and get the final patchedResources that are patched successfully
                    var patchedResources = new ConcurrentDictionary<string, (bool, ResourceElement)>();
                    ApplyPatchToResources(patchExceptions, searchResults, commonPatchFailures, patchFailures, commonPatchFailureReasons, conditionalPatchResourceRequests, patchedResources, cancellationToken);

                    await FinalizePatchResultsAndAuditAsync(resourceType, finalBulkUpdateResult, resourcesIgnored, commonPatchFailures, patchFailures, patchExceptions);

                    // Let's create the update tasks for the patched resources.
                    if (patchedResources.Any())
                    {
                        updateTasks.Add(UpdateResourcePage(patchedResources, resourceType, bundleResourceContext, searchResults, finalBulkUpdateResult, cancellationTokenSource.Token));
                        if (updateTasks.Any((task) => task.IsFaulted || task.IsCanceled))
                        {
                            break;
                        }

                        resourceTypesUpdated = AppendUpdateResults(resourceTypesUpdated, updateTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));
                        AppendUpdateResults(finalBulkUpdateResult.ResourcesUpdated as Dictionary<string, long>, updateTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));

                        updateTasks = updateTasks.Where(task => !task.IsCompletedSuccessfully).ToList();

                        if (updateTasks.Count >= parallelThreads)
                        {
                            await updateTasks[0];
                        }
                    }

                    // For resources that are included
                    if (!isIncludesRequest && !string.IsNullOrEmpty(ict) && AreIncludeResultsTruncated() && readNextPage && pageOne)
                    {
                        // run a search for included results
                        (resourceTypesUpdated, finalBulkUpdateResult) = await HandleIncludedResources(resourceType, fhirPatchParameters, parallelThreads, readNextPage, maximumNumberOfResourcesPerQuery, conditionalParameters, bundleResourceContext, ct, ict, resourceTypesUpdated, finalBulkUpdateResult, cancellationToken);
                    }

                    pageOne = false;

                    // Keep reading the next page of results if there are more results to process and when it is not a continuation token level job
                    if (!string.IsNullOrEmpty(ct) && readNextPage)
                    {
                        var cloneList = new List<Tuple<string, string>>(conditionalParameters);
                        if (isIncludesRequest)
                        {
                            // for includerequests, keep ct as is and remove the includesContinuationToken and add new includesContinuationToken to the cloneList
                            cloneList.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken, StringComparison.OrdinalIgnoreCase));
                            cloneList.Add(Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, ContinuationTokenEncoder.Encode(ict)));
                        }
                        else
                        {
                            cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(ct)));
                        }

                        searchResult = await Search(resourceType, isIncludesRequest, cloneList, cancellationToken);
                        ict = searchResult.IncludesContinuationToken;

                        // For resources that are included
                        if (!isIncludesRequest && !string.IsNullOrEmpty(ict) && AreIncludeResultsTruncated() && readNextPage)
                        {
                            // run a search for included results
                            (resourceTypesUpdated, finalBulkUpdateResult) = await HandleIncludedResources(resourceType, fhirPatchParameters, parallelThreads - updateTasks.Count, readNextPage, maximumNumberOfResourcesPerQuery, conditionalParameters, bundleResourceContext, ct, ict, resourceTypesUpdated, finalBulkUpdateResult, cancellationToken);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating");
                await cancellationTokenSource.CancelAsync();
            }

            try
            {
                // We need to wait until all running tasks are cancelled to get a count of resources deleted.
                await Task.WhenAll(updateTasks);
            }
            catch (AggregateException age) when (age.InnerExceptions.Any(e => e is not TaskCanceledException))
            {
                // If one of the tasks fails, the rest may throw a cancellation exception. Filtering those out as they are noise.
                foreach (var coreException in age.InnerExceptions.Where(e => e is not TaskCanceledException))
                {
                    _logger.LogError(coreException, "Error updating");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating");
            }

            resourceTypesUpdated = AppendUpdateResults(resourceTypesUpdated, updateTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));
            AppendUpdateResults(finalBulkUpdateResult.ResourcesUpdated as Dictionary<string, long>, updateTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));

            if (updateTasks.Any((task) => task.IsFaulted || task.IsCanceled) || tooManyIncludeResults)
            {
                var exceptions = new List<Exception>();

                if (tooManyIncludeResults)
                {
                    exceptions.Add(new BadRequestException(string.Format(CultureInfo.InvariantCulture, Core.Resources.TooManyIncludeResults, _configuration.DefaultIncludeCountPerSearch, _configuration.MaxIncludeCountPerSearch)));
                }

                updateTasks.Where((task) => task.IsFaulted || task.IsCanceled).ToList().ForEach((Task<Dictionary<string, long>> result) =>
                {
                    if (result.Exception != null)
                    {
                        // Count the number of resources updated before the exception was thrown. Update the total.
                        if (result.Exception.InnerExceptions.Any(ex => ex is IncompleteOperationException<BulkUpdateResult>))
                        {
                            var resourcesUpdated = result.Exception.InnerExceptions.Where((ex) => ex is IncompleteOperationException<BulkUpdateResult>)
                                    .Select(ex => ((IncompleteOperationException<BulkUpdateResult>)ex).PartialResults.ResourcesUpdated as Dictionary<string, long>);
                            AppendUpdateResults(resourceTypesUpdated, resourcesUpdated);
                            AppendUpdateResults(finalBulkUpdateResult.ResourcesUpdated as Dictionary<string, long>, resourcesUpdated);
                        }

                        if (result.IsFaulted)
                        {
                            // Filter out noise from the cancellation exceptions caused by the core exception.
                            exceptions.AddRange(result.Exception.InnerExceptions.Where(e => e is not TaskCanceledException));
                        }
                    }
                });

                var aggregateException = new AggregateException(exceptions);
                throw new IncompleteOperationException<BulkUpdateResult>(aggregateException, finalBulkUpdateResult);
            }

            // Do not stop processing if there are any patch exceptions
            return finalBulkUpdateResult;
        }

        private async Task FinalizePatchResultsAndAuditAsync(string resourceType, BulkUpdateResult finalBulkUpdateResult, Dictionary<string, long> resourcesIgnored, Dictionary<string, long> commonPatchFailures, ConcurrentDictionary<string, long> patchFailures, ConcurrentDictionary<string, List<(string id, Exception exception)>> patchExceptions)
        {
            // Let's update finalBulkUpdateResult with current page patch results commonPatchFailures, patchFailures, resourcesIgnored
            AppendUpdateResults(finalBulkUpdateResult.ResourcesIgnored as Dictionary<string, long>, [resourcesIgnored]);
            AppendUpdateResults(finalBulkUpdateResult.ResourcesPatchFailed as Dictionary<string, long>, [commonPatchFailures]);
            foreach (var newResult in patchFailures)
            {
                if (!finalBulkUpdateResult.ResourcesPatchFailed.TryAdd(newResult.Key, newResult.Value))
                {
                    finalBulkUpdateResult.ResourcesPatchFailed[newResult.Key] += newResult.Value;
                }

                patchFailures[newResult.Key] = 0; // reset the count for the next page
            }

            // Log Patch failed resources to the audit log
            if (patchExceptions.Any())
            {
                await CreateAuditLog(
                    resourceType,
                    false,
                    patchExceptions.SelectMany(kvp => kvp.Value.Select(item => (resourceType: kvp.Key, id: item.id, isInclude: true))).ToList(),
                    typeOfAudit: "Patch Failed Items");

                // reset PatchExceptions for the next page
                patchExceptions.Clear();
            }

            // reset resourcesIgnored for the next page
            foreach (var ri in resourcesIgnored)
            {
                resourcesIgnored[ri.Key] = 0; // reset the count for the next page
            }

            // reset commonPatchFailures for the next page
            foreach (var pf in commonPatchFailures)
            {
                commonPatchFailures[pf.Key] = 0; // reset the count for the next page
            }
        }

        private async Task<(Dictionary<string, long> resourceTypesUpdated, BulkUpdateResult finalBulkUpdateResult)> HandleIncludedResources(string resourceType, string fhirPatchParameters, int parallelThreads, bool readNextPage, uint maximumNumberOfResourcesPerQuery, IReadOnlyList<Tuple<string, string>> conditionalParameters, BundleResourceContext bundleResourceContext, string ct, string ict, Dictionary<string, long> resourceTypesUpdated, BulkUpdateResult finalBulkUpdateResult, CancellationToken cancellationToken)
        {
            var cloneList = new List<Tuple<string, string>>(conditionalParameters);
            cloneList.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.ContinuationToken, StringComparison.OrdinalIgnoreCase));
            cloneList.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken, StringComparison.OrdinalIgnoreCase));

            cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(ct)));
            cloneList.Add(Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, ContinuationTokenEncoder.Encode(ict)));

            var subResult = await UpdateMultipleAsync(resourceType, fhirPatchParameters, parallelThreads, readNextPage, maximumNumberOfResourcesPerQuery, true, cloneList, bundleResourceContext, cancellationToken);
            resourceTypesUpdated = AppendUpdateResults(resourceTypesUpdated, new List<Dictionary<string, long>>() { new Dictionary<string, long>(subResult.ResourcesUpdated) });
            finalBulkUpdateResult = AppendBulkUpdateResultsFromSubResults(finalBulkUpdateResult, subResult);
            return (resourceTypesUpdated, finalBulkUpdateResult);
        }

        private async Task<SearchResult> Search(string resourceType, bool isIncludesRequest, IReadOnlyList<Tuple<string, string>> conditionalParameters, CancellationToken cancellationToken)
        {
            using (var searchService = _searchServiceFactory.Invoke())
            {
                return await searchService.Value.SearchAsync(
                    resourceType,
                    conditionalParameters,
                    cancellationToken,
                    false,
                    resourceVersionTypes: ResourceVersionType.Latest,
                    onlyIds: false,
                    isIncludesOperation: isIncludesRequest);
            }
        }

        private void BuildConditionalPatchRequests(
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            BundleResourceContext bundleResourceContext,
            IReadOnlyCollection<SearchResultEntry> searchResults,
            Dictionary<string, long> totalResources,
            Dictionary<string, long> resourcesIgnored,
            Dictionary<string, long> commonPatchFailures,
            Dictionary<string, ConditionalPatchResourceRequest> conditionalPatchResourceRequests,
            Hl7.Fhir.Model.Parameters fhirPatchParameters)
        {
            // searchResults could return resources of same resource type or differenet
            // We need to group by resource type and create applicable patchParameters

            // Get total resources by resource type for this page
            Dictionary<string, long> resourcesPerPage = searchResults
                .GroupBy(res => res.Resource.ResourceTypeName)
                .ToDictionary(group => group.Key, group => (long)group.Count());

            // Get total resources by resource type by adding to the existing value
            foreach (var group in resourcesPerPage)
            {
                totalResources[group.Key] = totalResources.TryGetValue(group.Key, out var existing)
                    ? existing + group.Value
                    : group.Value;
            }

            // Add resources ignored by resource type by filtering out the excluded resource types
            foreach (var kvp in resourcesPerPage.Where(kvp => _excludedResourceTypes.Contains(kvp.Key)))
            {
                if (resourcesIgnored.TryGetValue(kvp.Key, out long existingValue))
                {
                    resourcesIgnored[kvp.Key] = existingValue + kvp.Value;
                }
                else
                {
                    resourcesIgnored[kvp.Key] = kvp.Value;
                }
            }

            // Filter the resourcesPerPage by removing the excluded resource types
            foreach (var resource in resourcesIgnored)
            {
                resourcesPerPage.Remove(resource.Key);
            }

            // Filter the resourcesPerPage by removing the commonPatchFailures
            foreach (var resource in commonPatchFailures)
            {
                resourcesPerPage.Remove(resource.Key);
            }

            // Build conditionalPatchResourceRequests
            var filteredResourceTypes = resourcesPerPage.Keys
                .Where(distinctResourceTypeOnPage => !conditionalPatchResourceRequests.ContainsKey(distinctResourceTypeOnPage))
                .ToList();

            foreach (var distinctResourceTypeOnPage in filteredResourceTypes)
            {
                var newListOfFhirPatchParameters = fhirPatchParameters.Parameter
                    .Where(param =>
                    {
                        var pathValue = param.Part
                            .FirstOrDefault(p => p.Name.Equals("path", StringComparison.Ordinal))?.Value?.ToString();

                        return pathValue != null &&
                               (pathValue.StartsWith(distinctResourceTypeOnPage, StringComparison.InvariantCultureIgnoreCase) ||
                                pathValue.StartsWith("Resource", StringComparison.InvariantCultureIgnoreCase));
                    })
                    .ToList();

                // Prepare the new conditional patch request for the distinct resource type only when there are applicable parameters
                if (newListOfFhirPatchParameters.Any())
                {
                    var newParameters = new Hl7.Fhir.Model.Parameters
                    {
                        Parameter = newListOfFhirPatchParameters,
                    };

                    var conditionalPatchResourceRequestOut = new ConditionalPatchResourceRequest(distinctResourceTypeOnPage, new FhirPathPatchPayload(newParameters), conditionalParameters, bundleResourceContext);
                    conditionalPatchResourceRequests[distinctResourceTypeOnPage] = conditionalPatchResourceRequestOut;
                }
                else
                {
                    // since there is no applicable parameters for this resource type, we can ignore it and add to resourcesIgnored with its count
                    // distinctResourceType could be a resource type from excludedResourceTypes, so we need to check if it exists in resourcesIgnored before adding it
                    if (!resourcesIgnored.TryGetValue(distinctResourceTypeOnPage, out long count))
                    {
                        resourcesIgnored[distinctResourceTypeOnPage] = totalResources[distinctResourceTypeOnPage];
                    }
                }
            }
        }

        private static void ApplyPatchToResources(
            ConcurrentDictionary<string, List<(string id, Exception ex)>> patchExceptions,
            IReadOnlyCollection<SearchResultEntry> searchResults,
            Dictionary<string, long> commonPatchFailures,
            ConcurrentDictionary<string, long> patchFailures,
            Dictionary<string, string> commonPatchFailureReasons,
            Dictionary<string, ConditionalPatchResourceRequest> conditionalPatchResourceRequests,
            ConcurrentDictionary<string, (bool IsInclude, ResourceElement ResourceElement)> patchedResources,
            CancellationToken cancellationToken)
        {
            foreach (var group in searchResults.GroupBy(sr => sr.Resource.ResourceTypeName))
            {
                var resourceTypeFromSearchResults = group.Key;
                var resourceList = group.ToList();

                if (!conditionalPatchResourceRequests.TryGetValue(resourceTypeFromSearchResults, out var patchRequest))
                {
                    continue;
                }

                try
                {
                    // Test a single resource from the group.
                    // Catch the exceptions which would lead to all the resources of type X to fail the patch operation.
                    patchRequest.Payload.Patch(resourceList[0].Resource);
                }
                catch (RequestNotValidException ex) when (ex.Message.Equals(Core.Resources.PatchImmutablePropertiesIsNotValid, StringComparison.OrdinalIgnoreCase)
                || ex.Message.StartsWith("Invalid input for", StringComparison.OrdinalIgnoreCase)
                || ex.Message.StartsWith("While building a POCO:", StringComparison.OrdinalIgnoreCase))
                {
                    // Core.Resources.PatchImmutablePropertiesIsNotValid => PatchPayload.ImmutableProperties "Resource.id", "Resource.meta.lastUpdated", "Resource.meta.versionId", "Resource.text.div", "Resource.text.status"
                    // Invalid input for path => patient.birthdate, value=not-a-date
                    // While building a POCO: => path=patient.gender, value=not-a-gender
                    // Remember the error for this resource type and skip processing the entire group.
                    commonPatchFailureReasons[resourceTypeFromSearchResults] = ex.Message;
                    commonPatchFailures[resourceTypeFromSearchResults] = resourceList.Count;

                    // Record patchExceptions for all resources in this group.
                    var listOfErrors = resourceList.Select(sr => (sr.Resource.ResourceId, (Exception)null)).ToList();

                    patchExceptions.AddOrUpdate(
                        resourceTypeFromSearchResults,
                        listOfErrors,
                        (_, existing) =>
                        {
                            existing.AddRange(listOfErrors);
                            return existing;
                        });

                    continue;
                }
                catch (Exception)
                {
                    // Exception because something uncommon for this first resource
                    // This is fine let's try to run the Patch on all the resources of this type in parallel
                    // This exception will be catched later in the Parallel.ForEach loop
                }

                // If the test passed or uncommon error then, patch the entire group in parallel.
                var listOfErrorsOnIndividualPatch = new ConcurrentBag<(string, Exception)>();
                Parallel.ForEach(resourceList, new ParallelOptions { MaxDegreeOfParallelism = resourceList.Count, CancellationToken = cancellationToken }, (searchResult, cancel) =>
                {
                    try
                    {
                        var patchedResource = patchRequest.Payload.Patch(searchResult.Resource);
                        patchedResources.TryAdd(searchResult.Resource.ResourceId, (searchResult.SearchEntryMode == ValueSets.SearchEntryMode.Include, patchedResource));
                    }
                    catch (Exception ex)
                    {
                        patchFailures.AddOrUpdate(
                            searchResult.Resource.ResourceTypeName,
                            1,
                            (_, count) => count + 1);

                        listOfErrorsOnIndividualPatch.Add((searchResult.Resource.ResourceId, ex));
                    }
                });

                // If there are any errors in the listOfErrorsOnIndividualPatch, then we can add it to the patchExceptions
                if (!listOfErrorsOnIndividualPatch.IsEmpty)
                {
                    var errorsList = listOfErrorsOnIndividualPatch.ToList();
                    patchExceptions.AddOrUpdate(
                        resourceTypeFromSearchResults,
                        errorsList,
                        (_, existing) =>
                        {
                            existing.AddRange(errorsList);
                            return existing;
                        });
                }
            }
        }

        private async Task<Dictionary<string, long>> UpdateResourcePage(
            ConcurrentDictionary<string, (bool IsInclude, ResourceElement ResourceElement)> patchedResources,
            string resourceType,
            BundleResourceContext bundleResourceContext,
            IReadOnlyCollection<SearchResultEntry> resourcesToUpdate,
            BulkUpdateResult bulkUpdateResultsSoFar,
            CancellationToken cancellationToken)
        {
            await CreateAuditLog(
                resourceType,
                false,
                patchedResources.Select((item) => (item.Value.ResourceElement.InstanceType, item.Key, item.Value.IsInclude)));

            ResourceWrapperOperation[] wrapperOperations = await Task.WhenAll(patchedResources.Select(async item =>
            {
                // If there isn't a cached capability statement (IE this is the first request made after a service starts up) then performance on this request will be terrible as the capability statement needs to be rebuilt for every resource.
                // This is because the capability statement can't be made correctly in a background job, so it doesn't cache the result.
                // The result is good enough for background work, but can't be used for metadata as the urls aren't formated properly.
                bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(item.Value.ResourceElement.InstanceType, cancellationToken);
                ResourceWrapper updateWrapper = CreateUpdateWrapper(patchedResources[item.Key].ResourceElement);
                return new ResourceWrapperOperation(updateWrapper, true, keepHistory, null, false, false, bundleResourceContext: bundleResourceContext);
            }));

            var partialResults = new List<(string, string, bool)>();
            try
            {
                using var fhirDataStore = _fhirDataStoreFactory.Invoke();
                await fhirDataStore.Value.MergeAsync(wrapperOperations, cancellationToken);
            }
            catch (IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> ex)
            {
                _logger.LogError(ex.InnerException, "Error updating");

                var ids = ex.PartialResults.Select(item => (
                    item.Key.ResourceType,
                    item.Key.Id,
                    patchedResources
                        .Where(resource => resource.Key == item.Key.Id && resource.Value.ResourceElement.InstanceType == item.Key.ResourceType)
                        .FirstOrDefault().Value.IsInclude)).ToList();

                ids.AddRange(partialResults);

                var resourceTypesUpdated = ids.GroupBy(pair => pair.ResourceType).ToDictionary(group => group.Key, group => (long)group.Count());

                // check the dictionary bulkUpdateResultsSoFar.ResourcesUpdated and add new values for resourceTypesUpdated
                AppendUpdateResults(bulkUpdateResultsSoFar.ResourcesUpdated as Dictionary<string, long>, (IEnumerable<Dictionary<string, long>>)resourceTypesUpdated);

                await CreateAuditLog(resourceType, true, ids);
                throw new IncompleteOperationException<BulkUpdateResult>(
                    ex.InnerException,
                    bulkUpdateResultsSoFar);
            }

            await CreateAuditLog(
                resourceType,
                true,
                patchedResources.Select((item) => (item.Value.ResourceElement.InstanceType, item.Key, item.Value.IsInclude)));

            return patchedResources.GroupBy(x => x.Value.ResourceElement.InstanceType).ToDictionary(x => x.Key, x => (long)x.Count());
        }

        private ResourceWrapper CreateUpdateWrapper(ResourceElement resourceElement)
        {
            ResourceWrapper updateWrapper = _resourceWrapperFactory.CreateResourceWrapper(resourceElement.ToPoco<Resource>(), _resourceIdProvider, deleted: false, keepMeta: true);
            return updateWrapper;
        }

        private System.Threading.Tasks.Task CreateAuditLog(string primaryResourceType, bool complete, IEnumerable<(string resourceType, string resourceId, bool included)> items, HttpStatusCode statusCode = HttpStatusCode.OK, string typeOfAudit = "Affected Items")
        {
            var auditTask = System.Threading.Tasks.Task.Run(() =>
            {
                AuditAction action = complete ? AuditAction.Executed : AuditAction.Executing;
                var context = _contextAccessor.RequestContext;
                var updateAdditionalProperties = new Dictionary<string, string>();
                updateAdditionalProperties[typeOfAudit] = items.Aggregate(
                    string.Empty,
                    (aggregate, item) =>
                    {
                        aggregate += ", " + (item.included ? "[Include] " : string.Empty) + item.resourceType + "/" + item.resourceId;
                        return aggregate;
                    });

                _auditLogger.LogAudit(
                    auditAction: action,
                    operation: "Update",
                    resourceType: primaryResourceType,
                    requestUri: context.Uri,
                    statusCode: statusCode,
                    correlationId: context.CorrelationId,
                    callerIpAddress: string.Empty,
                    callerClaims: null,
                    customHeaders: null,
                    operationType: string.Empty,
                    callerAgent: DefaultCallerAgent,
                    additionalProperties: updateAdditionalProperties);
            });

            return auditTask;
        }

        private bool AreIncludeResultsTruncated()
        {
            return _contextAccessor.RequestContext.BundleIssues.Any(
                x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, long> AppendUpdateResults(Dictionary<string, long> results, IEnumerable<Dictionary<string, long>> newResults)
        {
            foreach (var newResult in newResults)
            {
                foreach (var (key, value) in newResult)
                {
                    if (!results.TryAdd(key, value))
                    {
                        results[key] += value;
                    }
                }
            }

            return results;
        }

        private static BulkUpdateResult AppendBulkUpdateResultsFromSubResults(BulkUpdateResult result, BulkUpdateResult newResult)
        {
            AppendUpdateResults(result.ResourcesUpdated as Dictionary<string, long>, new[] { new Dictionary<string, long>(newResult.ResourcesUpdated) });
            AppendUpdateResults(result.ResourcesIgnored as Dictionary<string, long>, new[] { new Dictionary<string, long>(newResult.ResourcesIgnored) });
            AppendUpdateResults(result.ResourcesPatchFailed as Dictionary<string, long>, new[] { new Dictionary<string, long>(newResult.ResourcesPatchFailed) });

            return result;
        }
    }
}
