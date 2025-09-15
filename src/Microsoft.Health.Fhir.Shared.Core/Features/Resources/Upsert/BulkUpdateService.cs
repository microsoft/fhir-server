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
using System.Reflection.Metadata.Ecma335;
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
    /// <summary>
    /// Provides bulk update functionality for FHIR resources, supporting conditional patching and parallel processing.
    /// This service coordinates searching, patching, and updating resources in batches, handling included resources, error aggregation, and audit logging.
    /// It ensures robust exception handling and efficient resource updates across multiple resource types and pages.
    /// </summary>
    public class BulkUpdateService : IBulkUpdateService
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly Lazy<IConformanceProvider> _conformanceProvider;
        private readonly IScopeProvider<IFhirDataStore> _fhirDataStoreFactory;
        private readonly IScopeProvider<ISearchService> _searchServiceFactory;
        private readonly ResourceIdProvider _resourceIdProvider;
        private readonly FhirRequestContextAccessor _contextAccessor;
        private readonly IAuditLogger _auditLogger;
        private readonly ILogger<BulkUpdateService> _logger;

        internal const string DefaultCallerAgent = "Microsoft.Health.Fhir.Server";

        public BulkUpdateService(
            IResourceWrapperFactory resourceWrapperFactory,
            Lazy<IConformanceProvider> conformanceProvider,
            IScopeProvider<IFhirDataStore> fhirDataStoreFactory,
            IScopeProvider<ISearchService> searchServiceFactory,
            ResourceIdProvider resourceIdProvider,
            FhirRequestContextAccessor contextAccessor,
            IAuditLogger auditLogger,
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
        }

        /// <summary>
        /// Performs bulk updates on multiple FHIR resources using conditional patch parameters.
        /// Executes search, patch, and update operations in parallel, supporting continuation for paged results.
        /// For non-page-level jobs, reads all result pages and processes them in reverse order to maintain update consistency
        /// And to fit in the logic for both workflows parallel vs non-parallel.
        /// Prioritizes updating included resources before matched resources to prevent invalid continuation tokens due to surrogate ID changes for matched resources.
        /// For included resources, processes from the last page backward, as _lastUpdated is only applied to matched resources.
        /// This approach avoids returning already updated included resources in subsequent searches and ensures accurate bulk updates.
        /// Handles batching, error aggregation, and audit logging throughout the operation.
        /// </summary>
        public async Task<BulkUpdateResult> UpdateMultipleAsync(string resourceType, string fhirPatchParameters, bool readNextPage, uint readUpto, bool isIncludesRequest, IReadOnlyList<Tuple<string, string>> conditionalParameters, BundleResourceContext bundleResourceContext, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<SearchResultEntry> searchResults;
            SearchResult searchResult;
            string ct;
            string ict;

            BulkUpdateResult finalBulkUpdateResult = new BulkUpdateResult();
            Dictionary<string, long> totalResources = new Dictionary<string, long>();
            Dictionary<string, long> resourcesIgnored = new Dictionary<string, long>(); // contains the count of resources that have no valid patch parameters or excluded resource types
            Dictionary<string, long> commonPatchFailures = new Dictionary<string, long>();
            Dictionary<string, string> commonPatchFailureReasons = new Dictionary<string, string>();
            ConcurrentDictionary<string, long> patchFailures = new ConcurrentDictionary<string, long>();
            Dictionary<string, ConditionalPatchResourceRequest> conditionalPatchResourceRequests = new Dictionary<string, ConditionalPatchResourceRequest>();
            ConcurrentDictionary<string, List<(string, Exception)>> patchExceptions = new ConcurrentDictionary<string, List<(string, Exception)>>();
            var updateTasks = new List<Task<Dictionary<string, long>>>();
            using var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            searchResult = await Search(resourceType, isIncludesRequest, conditionalParameters, cancellationToken);
            if (!searchResult.Results?.Any() ?? true)
            {
                _logger.LogInformation("No resources found for bulk update operation for resource type {ResourceType}.", resourceType);
                return finalBulkUpdateResult;
            }

            // Deserialize the FHIR patch parameters from the payload
            var fhirJsonParser = new FhirJsonParser();
            var deserializedFhirPatchParameters = await fhirJsonParser.ParseAsync<Hl7.Fhir.Model.Parameters>(fhirPatchParameters);

            try
            {
                if (searchResult.Results != null && searchResult.Results.Any())
                {
                    ct = isIncludesRequest ? searchResult.IncludesContinuationToken : searchResult.ContinuationToken;
                    ict = searchResult.IncludesContinuationToken;
                    searchResults = searchResult.Results.ToList();

                    // For resources that are included
                    if (!isIncludesRequest && !string.IsNullOrEmpty(ict) && AreIncludeResultsTruncated())
                    {
                        // run a search for included results
                        finalBulkUpdateResult = await HandleIncludedResources(resourceType, fhirPatchParameters, true, conditionalParameters, bundleResourceContext, ct, ict, finalBulkUpdateResult, cancellationToken);
                    }

                    // Keep reading the next page of results if there are more results to process and when it is not a continuation token level job
                    // Or when readUpto is set for CT level jobs and we have not reached that limit yet
                    if ((!string.IsNullOrEmpty(ct) && readNextPage) || (!string.IsNullOrEmpty(ct) && !readNextPage && readUpto > 1))
                    {
                        var cloneList = new List<Tuple<string, string>>(conditionalParameters);
                        if (isIncludesRequest)
                        {
                            // for include requests, keep ct as is and remove the includesContinuationToken and add new includesContinuationToken to the cloneList
                            cloneList.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken, StringComparison.OrdinalIgnoreCase));
                            cloneList.Add(Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, ContinuationTokenEncoder.Encode(ict)));
                        }
                        else
                        {
                            cloneList.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.ContinuationToken, StringComparison.OrdinalIgnoreCase));
                            cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(ct)));
                        }

                        readUpto--;
                        var subResult = await UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, readUpto, isIncludesRequest, cloneList, bundleResourceContext, cancellationToken);
                        finalBulkUpdateResult = AppendBulkUpdateResultsFromSubResults(finalBulkUpdateResult, subResult);
                        _logger.LogInformation("Bulk updated total {Count} resources for the page.", subResult.ResourcesUpdated.Sum(resource => resource.Value));
                    }

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
                        if (!updateTasks.Any((task) => task.IsFaulted || task.IsCanceled))
                        {
                            AppendUpdateResults(finalBulkUpdateResult.ResourcesUpdated as Dictionary<string, long>, updateTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));

                            updateTasks = updateTasks.Where(task => !task.IsCompletedSuccessfully).ToList();
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("No resources found for bulk update operation for resource type {ResourceType}.", resourceType);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating");
                await cancellationTokenSource.CancelAsync();
            }

            try
            {
                // We need to wait until all running tasks are cancelled or completed to get a count of resources updated.
                await Task.WhenAll(updateTasks);
            }
            catch (AggregateException age) when (age.InnerExceptions.Any(e => e is not TaskCanceledException))
            {
                // If one of the tasks fails, the rest may throw a cancellation exception. Filtering those out as they are noise.
                foreach (var coreException in age.InnerExceptions.Where(e => e is not TaskCanceledException))
                {
                    _logger.LogError(coreException, "Aggregate exception during bulk update operation while waiting for all update tasks");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generic exception during bulk update operation while waiting for all update tasks");
            }

            AppendUpdateResults(finalBulkUpdateResult.ResourcesUpdated as Dictionary<string, long>, updateTasks.Where(x => x.IsCompletedSuccessfully).Select(task => task.Result));

            if (updateTasks.Any((task) => task.IsFaulted || task.IsCanceled))
            {
                var exceptions = new List<Exception>();
                updateTasks.Where((task) => task.IsFaulted || task.IsCanceled).ToList().ForEach((Task<Dictionary<string, long>> result) =>
                {
                    if (result.Exception != null)
                    {
                        // Count the number of resources updated before the exception was thrown. Update the total.
                        if (result.Exception.InnerExceptions.Any(ex => ex is IncompleteOperationException<BulkUpdateResult>))
                        {
                            var resourcesUpdated = result.Exception.InnerExceptions.Where((ex) => ex is IncompleteOperationException<BulkUpdateResult>)
                                    .Select(ex => ((IncompleteOperationException<BulkUpdateResult>)ex).PartialResults.ResourcesUpdated as Dictionary<string, long>);
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

        /// <summary>
        /// Finalizes patch results for a page, updates audit logs, and resets tracking dictionaries for the next page.
        /// </summary>
        private async Task FinalizePatchResultsAndAuditAsync(string resourceType, BulkUpdateResult finalBulkUpdateResult, Dictionary<string, long> resourcesIgnored, Dictionary<string, long> commonPatchFailures, ConcurrentDictionary<string, long> patchFailures, ConcurrentDictionary<string, List<(string id, Exception exception)>> patchExceptions)
        {
            // Let's update finalBulkUpdateResult with current page patch results commonPatchFailures, patchFailures, resourcesIgnored
            AppendUpdateResults(finalBulkUpdateResult.ResourcesIgnored as Dictionary<string, long>, new[] { resourcesIgnored });
            AppendUpdateResults(finalBulkUpdateResult.ResourcesPatchFailed as Dictionary<string, long>, new[] { commonPatchFailures });
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

        /// <summary>
        /// Handles included resources in bulk update by recursively processing continuation tokens and aggregating results.
        /// </summary>
        private async Task<BulkUpdateResult> HandleIncludedResources(string resourceType, string fhirPatchParameters, bool readNextPage, IReadOnlyList<Tuple<string, string>> conditionalParameters, BundleResourceContext bundleResourceContext, string ct, string ict, BulkUpdateResult finalBulkUpdateResult, CancellationToken cancellationToken)
        {
            var cloneList = new List<Tuple<string, string>>(conditionalParameters);
            cloneList.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.ContinuationToken, StringComparison.OrdinalIgnoreCase));
            cloneList.RemoveAll(t => t.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken, StringComparison.OrdinalIgnoreCase));

            cloneList.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenEncoder.Encode(ct)));
            cloneList.Add(Tuple.Create(KnownQueryParameterNames.IncludesContinuationToken, ContinuationTokenEncoder.Encode(ict)));

            var subResult = await UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, true, cloneList, bundleResourceContext, cancellationToken);
            finalBulkUpdateResult = AppendBulkUpdateResultsFromSubResults(finalBulkUpdateResult, subResult);
            return finalBulkUpdateResult;
        }

        /// <summary>
        /// Executes a search for resources to be updated, supporting includes and conditional parameters.
        /// </summary>
        private async Task<SearchResult> Search(string resourceType, bool isIncludesRequest, IReadOnlyList<Tuple<string, string>> conditionalParameters, CancellationToken cancellationToken)
        {
            using (var searchService = _searchServiceFactory.Invoke())
            {
                var searchResults = await searchService.Value.SearchAsync(
                    resourceType,
                    conditionalParameters,
                    cancellationToken,
                    true,
                    resourceVersionTypes: ResourceVersionType.Latest,
                    onlyIds: false,
                    isIncludesOperation: isIncludesRequest);

                if (searchResults != null && searchResults.Results.Any())
                {
                    // When running the search with surrogate IDs, and if resource becomes historical after the job is started then
                    // the search returns historical record, we need to filter out the history resources
                    searchResults = new SearchResult(
                        searchResults.Results.Where(r => !r.Resource.IsHistory),
                        searchResults.ContinuationToken,
                        searchResults.SortOrder,
                        searchResults.UnsupportedSearchParameters,
                        searchResults.SearchIssues,
                        searchResults.IncludesContinuationToken);
                }

                return searchResults;
            }
        }

        /// <summary>
        /// Builds conditional patch requests for each resource type found in the search results,
        /// grouping applicable patch parameters and tracking ignored or failed resources.
        /// </summary>
        private static void BuildConditionalPatchRequests(
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

            // Add excluded resource types to resourcesIgnored and remove it from resourcesPerPage
            foreach (var kvp in resourcesPerPage.Where(kvp => OperationsConstants.ExcludedResourceTypesForBulkUpdate.Contains(kvp.Key)))
            {
                resourcesIgnored[kvp.Key] = resourcesIgnored.TryGetValue(kvp.Key, out var existing)
                    ? existing + kvp.Value
                    : kvp.Value;
                resourcesPerPage.Remove(kvp.Key);
            }

            // Use ResourcesIgnored keys that were found in last page and count the numbers for this page and remove it from resourcesPerPage
            foreach (var key in resourcesIgnored.Keys.Intersect(resourcesPerPage.Keys).ToList())
            {
                // Store the count of resources that should be ignored
                resourcesIgnored[key] = resourcesIgnored[key] + resourcesPerPage[key];
                resourcesPerPage.Remove(key);
            }

            // Filter the resourcesPerPage by removing the commonPatchFailures
            foreach (var key in commonPatchFailures.Keys.Intersect(resourcesPerPage.Keys).ToList())
            {
                // Store the count of resources that would fail to patch in commonPatchFailures
                commonPatchFailures[key] = commonPatchFailures[key] + resourcesPerPage[key];
                resourcesPerPage.Remove(key);
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

        /// <summary>
        /// Applies patch operations to resources in parallel, tracking failures and exceptions for each resource type.
        /// </summary>
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

        /// <summary>
        /// Updates a page of patched resources in the data store, logs audit events, and returns update counts by resource type.
        /// </summary>
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

            ResourceWrapperOperation[] wrapperOperationsIncludes = await Task.WhenAll(patchedResources.Where(pr => pr.Value.IsInclude).Select(async item =>
            {
                // If there isn't a cached capability statement (IE this is the first request made after a service starts up) then performance on this request will be terrible as the capability statement needs to be rebuilt for every resource.
                // This is because the capability statement can't be made correctly in a background job, so it doesn't cache the result.
                // The result is good enough for background work, but can't be used for metadata as the urls aren't formated properly.
                bool keepHistory = await _conformanceProvider.Value.CanKeepHistory(item.Value.ResourceElement.InstanceType, cancellationToken);
                ResourceWrapper updateWrapper = CreateUpdateWrapper(patchedResources[item.Key].ResourceElement);
                return new ResourceWrapperOperation(updateWrapper, true, keepHistory, null, false, false, bundleResourceContext: bundleResourceContext);
            }));

            ResourceWrapperOperation[] wrapperOperationsMatches = await Task.WhenAll(patchedResources.Where(pr => !pr.Value.IsInclude).Select(async item =>
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

                // Update includes first so that the reference for match result remains intact for next pages
                if (wrapperOperationsIncludes.Any())
                {
                    await fhirDataStore.Value.MergeAsync(wrapperOperationsIncludes, cancellationToken);
                }

                if (wrapperOperationsMatches.Any())
                {
                    await fhirDataStore.Value.MergeAsync(wrapperOperationsMatches, cancellationToken);
                }
            }
            catch (IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> ex)
            {
                _logger.LogError(ex.InnerException, "Error updating resources");

                var ids = ex.PartialResults.Select(item => (
                    item.Key.ResourceType,
                    item.Key.Id,
                    patchedResources
                        .Where(resource => resource.Key == item.Key.Id && resource.Value.ResourceElement.InstanceType == item.Key.ResourceType)
                        .FirstOrDefault().Value.IsInclude)).ToList();

                ids.AddRange(partialResults);

                var resourceTypesUpdated = ids.GroupBy(pair => pair.ResourceType).ToDictionary(group => group.Key, group => (long)group.Count());

                // return the new BulkUpdateResult with the resources updated, the final result will be updated later in the calling method
                var bulkUpdateResultsFromUpdateFlow = new BulkUpdateResult();
                AppendUpdateResults(bulkUpdateResultsFromUpdateFlow.ResourcesUpdated as Dictionary<string, long>, new[] { new Dictionary<string, long>(resourceTypesUpdated) });

                await CreateAuditLog(resourceType, true, ids);
                throw new IncompleteOperationException<BulkUpdateResult>(
                    ex.InnerException,
                    bulkUpdateResultsFromUpdateFlow);
            }

            await CreateAuditLog(
                resourceType,
                true,
                patchedResources.Select((item) => (item.Value.ResourceElement.InstanceType, item.Key, item.Value.IsInclude)));

            return patchedResources.GroupBy(x => x.Value.ResourceElement.InstanceType).ToDictionary(x => x.Key, x => (long)x.Count());
        }

        /// <summary>
        /// Creates a resource wrapper for an updated resource, preserving metadata and identifiers.
        /// </summary>
        private ResourceWrapper CreateUpdateWrapper(ResourceElement resourceElement)
        {
            ResourceWrapper updateWrapper = _resourceWrapperFactory.CreateResourceWrapper(resourceElement.ToPoco<Resource>(), _resourceIdProvider, deleted: false, keepMeta: true);
            return updateWrapper;
        }

        /// <summary>
        /// Creates an audit log entry for the bulk update operation, including affected resources and status.
        /// </summary>
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

        /// <summary>
        /// Determines if include results in the current request context are truncated.
        /// </summary>
        private bool AreIncludeResultsTruncated()
        {
            return _contextAccessor.RequestContext.BundleIssues.Any(
                x => string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessage, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Diagnostics, Core.Resources.TruncatedIncludeMessageForIncludes, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Appends update results from new resource counts to the existing results dictionary.
        /// </summary>
        private static Dictionary<string, long> AppendUpdateResults(Dictionary<string, long> results, IEnumerable<Dictionary<string, long>> newResults)
        {
            if (results == null)
            {
                results = new Dictionary<string, long>();
            }

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

        /// <summary>
        /// Aggregates bulk update results from sub-results into the main result object.
        /// </summary>
        private static BulkUpdateResult AppendBulkUpdateResultsFromSubResults(BulkUpdateResult result, BulkUpdateResult newResult)
        {
            AppendUpdateResults(result.ResourcesUpdated as Dictionary<string, long>, new[] { new Dictionary<string, long>(newResult.ResourcesUpdated) });
            AppendUpdateResults(result.ResourcesIgnored as Dictionary<string, long>, new[] { new Dictionary<string, long>(newResult.ResourcesIgnored) });
            AppendUpdateResults(result.ResourcesPatchFailed as Dictionary<string, long>, new[] { new Dictionary<string, long>(newResult.ResourcesPatchFailed) });

            return result;
        }
    }
}
