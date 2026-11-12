// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.FhirPackages
{
    /// <summary>
    /// Writes a package's SearchParameters directly as <c>Enabled</c> on a store that contains no data, together with the
    /// applied-package record, in one SQL transaction. Never starts a reindex.
    /// </summary>
    /// <remarks>
    /// The transaction asserts the empty-store precondition, then reuses <c>dbo.MergeResourcesAndSearchParams</c>, which
    /// rejects the write if a reindex job is active or the SearchParameter reference set changed since validation, and
    /// finally adds the applied-package record. Either everything commits or nothing does.
    /// <para>
    /// Known limitation: custom SearchParameters on the SearchParameter resource type aren't supported. The empty-store
    /// check ignores SearchParameter resources, so a package parameter whose base covers SearchParameter (directly or via
    /// Resource/DomainResource) is enabled without indexing SearchParameter resources already in the store, and searches
    /// on SearchParameter by that parameter miss them.
    /// </para>
    /// </remarks>
    public sealed class SqlServerSearchParameterPackageWriter : ISearchParameterPackageWriter
    {
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ITransactionHandler _transactionHandler;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ISqlServerFhirModel _model;
        private readonly IFhirSpecificationPackageRecordStore _recordStore;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ILogger<SqlServerSearchParameterPackageWriter> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlServerSearchParameterPackageWriter"/> class.
        /// </summary>
        /// <param name="fhirDataStore">Writes the SearchParameter resources and statuses.</param>
        /// <param name="transactionHandler">Starts the transaction that the whole write enlists in.</param>
        /// <param name="sqlConnectionWrapperFactory">Provides the connection enlisted in the write transaction for the empty-store check.</param>
        /// <param name="model">Resolves the SearchParameter resource type id.</param>
        /// <param name="recordStore">Reads and writes the applied-package record.</param>
        /// <param name="resourceWrapperFactory">Builds the stored form of each SearchParameter.</param>
        /// <param name="modelInfoProvider">Provides the structure definitions used to stamp server-assigned metadata.</param>
        /// <param name="searchParameterOperations">Refreshes this instance's SearchParameter cache after the write.</param>
        /// <param name="requestContextAccessor">The request context accessor.</param>
        /// <param name="logger">The logger.</param>
        public SqlServerSearchParameterPackageWriter(
            IFhirDataStore fhirDataStore,
            ITransactionHandler transactionHandler,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ISqlServerFhirModel model,
            IFhirSpecificationPackageRecordStore recordStore,
            IResourceWrapperFactory resourceWrapperFactory,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterOperations searchParameterOperations,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ILogger<SqlServerSearchParameterPackageWriter> logger)
        {
            _fhirDataStore = EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            _transactionHandler = EnsureArg.IsNotNull(transactionHandler, nameof(transactionHandler));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _recordStore = EnsureArg.IsNotNull(recordStore, nameof(recordStore));
            _resourceWrapperFactory = EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <inheritdoc />
        /// <exception cref="FhirPackageStoreNotEmptyException">The store contains data, so the parameters would need a reindex.</exception>
        /// <exception cref="FhirPackageWriteConflictException">The write conflicted with other state; see <see cref="FhirPackageWriteConflictException.IsRetryable"/>.</exception>
        public async Task WriteAsync(FhirPackage package, IReadOnlyList<ResourceElement> searchParameters, DateTimeOffset referenceSetLastUpdated, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(package, nameof(package));
            EnsureArg.IsNotNull(searchParameters, nameof(searchParameters));

            FhirPackageDescriptor descriptor = package.Descriptor;
            var record = new FhirSpecificationPackageRecord(
                descriptor.PackageId,
                descriptor.Version,
                descriptor.Integrity,
                searchParameters.Select(x => x.Instance.Children("url").FirstOrDefault()?.Value as string).ToList());

            if (!await IsAlreadyInstalledAsync(record, cancellationToken))
            {
                IFhirRequestContext originalContext = _requestContextAccessor.RequestContext;
                if (originalContext == null)
                {
                    _requestContextAccessor.RequestContext = CreateBackgroundContext();
                }

                try
                {
                    if (await WriteInTransactionAsync(record, searchParameters, referenceSetLastUpdated, cancellationToken))
                    {
                        _logger.LogInformation(
                            "Installed {Count} SearchParameter(s) from FHIR package {PackageId} version {Version} as Enabled without reindex.",
                            searchParameters.Count,
                            descriptor.PackageId,
                            descriptor.Version);
                    }
                }
                finally
                {
                    _requestContextAccessor.RequestContext = originalContext;
                }
            }

            // Refresh even when another writer installed the package, so this instance can search by its parameters now.
            // Other instances pick up the new parameters through their periodic SearchParameter refresh.
            await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
        }

        /// <returns><c>true</c> if this call committed the write; <c>false</c> if another writer installed the same package first.</returns>
        private async Task<bool> WriteInTransactionAsync(FhirSpecificationPackageRecord record, IReadOnlyList<ResourceElement> searchParameters, DateTimeOffset referenceSetLastUpdated, CancellationToken cancellationToken)
        {
            try
            {
                using ITransactionScope transaction = _transactionHandler.BeginTransaction();

                if (await ContainsDataAsync(cancellationToken))
                {
                    throw new FhirPackageStoreNotEmptyException(record.PackageId, record.Version);
                }

                List<ResourceWrapperOperation> operations = searchParameters
                    .Select((resource, i) => CreateOperation(resource, record.CanonicalUrls[i], referenceSetLastUpdated))
                    .ToList();

                MergeOutcome outcome = await _fhirDataStore.MergeAsync(operations, new MergeOptions(enlistTransaction: true, isBundleTransaction: true), cancellationToken);

                List<Exception> failures = outcome.Results.Values.Where(x => !x.IsOperationSuccessful).Select(x => (Exception)x.Exception).ToList();
                if (failures.Count > 0)
                {
                    // Nothing was persisted: a bundle-transaction merge validates every resource before it writes any.
                    if (await IsAlreadyInstalledAsync(record, cancellationToken))
                    {
                        return false;
                    }

                    throw new FhirPackageWriteConflictException(
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterResourceConflict, record.PackageId, record.Version, string.Join(" ", failures.Select(x => x.Message))),
                        isRetryable: false,
                        new AggregateException(failures));
                }

                await _recordStore.AddAsync(record, cancellationToken);

                transaction.Complete();
                return true;
            }
            catch (FhirPackageStoreNotEmptyException)
            {
                // Another writer may have installed this package before data arrived; that is the goal state, not a failure.
                if (await IsAlreadyInstalledAsync(record, cancellationToken))
                {
                    return false;
                }

                _logger.LogWarning(
                    "FHIR package {PackageId} version {Version} was not installed because the data store contains resources.",
                    record.PackageId,
                    record.Version);

                throw;
            }
            catch (Exception ex) when (IsConcurrencyConflict(ex))
            {
                // The transaction has rolled back. If the conflict was another writer installing this same package, the
                // goal state is reached.
                if (await IsAlreadyInstalledAsync(record, cancellationToken))
                {
                    return false;
                }

                _logger.LogWarning(ex, "FHIR package {PackageId} version {Version} conflicted with a concurrent SearchParameter change or reindex.", record.PackageId, record.Version);

                throw new FhirPackageWriteConflictException(
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageWriteConflictRetryable, record.PackageId, record.Version, ex.Message),
                    isRetryable: true,
                    ex);
            }
        }

        private ResourceWrapperOperation CreateOperation(ResourceElement searchParameter, string url, DateTimeOffset referenceSetLastUpdated)
        {
            ResourceWrapper wrapper = _resourceWrapperFactory.Create(PrepareForWrite(searchParameter), deleted: false, keepMeta: true);

            // createOnly: a package never overwrites a SearchParameter resource that already exists.
            return new ResourceWrapperOperation(
                wrapper,
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null,
                createOnly: true)
            {
                // The store is empty, so nothing needs reindexing and the parameter is searchable immediately.
                // LastUpdated is the validated reference-set version, which the merge uses as its optimistic-concurrency token.
                PendingSearchParameterStatus = new ResourceSearchParameterStatus
                {
                    Uri = new Uri(url),
                    Status = SearchParameterStatus.Enabled,
                    IsPartiallySupported = false,
                    SortStatus = SortParameterStatus.Disabled,
                    LastUpdated = referenceSetLastUpdated,
                },
            };
        }

        /// <summary>
        /// Sets the metadata a normal create would assign: an id if the package didn't declare one, and the current
        /// <c>meta.lastUpdated</c>, which the merge requires in the stored JSON.
        /// </summary>
        private ResourceElement PrepareForWrite(ResourceElement searchParameter)
        {
            var provider = _modelInfoProvider.StructureDefinitionSummaryProvider;
            ElementNode node = ElementNode.FromElement(searchParameter.Instance);

            if (string.IsNullOrWhiteSpace(searchParameter.Id))
            {
                foreach (ElementNode emptyId in node.Children("id").Cast<ElementNode>().ToList())
                {
                    node.Remove(emptyId);
                }

                node.Add(provider, "id", Guid.NewGuid().ToString());
            }

            ElementNode meta = node.Children("meta").Cast<ElementNode>().FirstOrDefault() ?? node.Add(provider, "meta");
            foreach (ElementNode lastUpdated in meta.Children("lastUpdated").Cast<ElementNode>().ToList())
            {
                meta.Remove(lastUpdated);
            }

            meta.Add(provider, "lastUpdated", Hl7.Fhir.ElementModel.Types.DateTime.FromDateTimeOffset(Clock.UtcNow.UtcDateTime.TruncateToMillisecond()));

            return new ResourceElement(node);
        }

        /// <summary>
        /// Applies the SearchParameter status initialization rule (new parameters are enabled only while <c>dbo.Resource</c>
        /// is empty), except that SearchParameter rows are ignored because earlier package installs write them.
        /// </summary>
        /// <remarks>
        /// Runs inside the write transaction. HOLDLOCK keeps range locks until it ends, so no counted row can be inserted
        /// before the package commits.
        /// </remarks>
        private async Task<bool> ContainsDataAsync(CancellationToken cancellationToken)
        {
            using SqlConnectionWrapper conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, enlistInTransaction: true);
            using var cmd = new SqlCommand
            {
                CommandType = CommandType.Text,
                CommandText = "SELECT TOP 1 1 FROM dbo.Resource WITH (HOLDLOCK) WHERE ResourceTypeId <> @SearchParameterTypeId",
                Connection = conn.SqlConnection,
                Transaction = conn.SqlTransaction,
            };
            cmd.Parameters.Add(new SqlParameter("@SearchParameterTypeId", SqlDbType.SmallInt) { Value = _model.GetResourceTypeId(KnownResourceTypes.SearchParameter) });

            return await cmd.ExecuteScalarAsync(cancellationToken) != null;
        }

        /// <returns><c>true</c> if the same content is already installed.</returns>
        /// <exception cref="FhirPackageWriteConflictException">A different version or integrity of the package is installed, or the lookup hit a transient SQL error.</exception>
        private async Task<bool> IsAlreadyInstalledAsync(FhirSpecificationPackageRecord record, CancellationToken cancellationToken)
        {
            FhirSpecificationPackageRecord installed;
            try
            {
                installed = await _recordStore.GetAsync(record.PackageId, cancellationToken);
            }
            catch (Exception ex) when (IsTransientSqlError(ex))
            {
                throw new FhirPackageWriteConflictException(
                    string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageWriteConflictRetryable, record.PackageId, record.Version, ex.Message),
                    isRetryable: true,
                    ex);
            }

            if (installed == null)
            {
                return false;
            }

            if (installed.IsSameContentAs(record))
            {
                _logger.LogInformation("FHIR package {PackageId} version {Version} is already installed.", record.PackageId, record.Version);
                return true;
            }

            throw new FhirPackageWriteConflictException(
                string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageInstalledWithDifferentContent, installed.PackageId, installed.Version, installed.Integrity, record.Version, record.Integrity),
                isRetryable: false);
        }

        private static bool IsConcurrencyConflict(Exception ex)
        {
            return ex is JobConflictException
                || ex is ResourceConflictException
                || (ex is BadRequestException && string.Equals(ex.Message, Core.Resources.SearchParameterConcurrencyConflict, StringComparison.Ordinal))
                || IsTransientSqlError(ex);
        }

        private static bool IsTransientSqlError(Exception ex)
        {
            // Same classification as SqlRetryService: transient error numbers plus transport and availability failures.
            return ex is SqlException sqlEx && (sqlEx.IsSqlTransientException() || sqlEx.IsRetriable());
        }

        private static FhirRequestContext CreateBackgroundContext()
        {
            // The method is stored in Resource.RequestMethod, varchar(10).
            return new FhirRequestContext(
                method: "POST",
                uriString: "https://localhost/SearchParameter",
                baseUriString: "https://localhost/",
                correlationId: Guid.NewGuid().ToString(),
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>())
            {
                IsBackgroundTask = true,
            };
        }
    }
}
