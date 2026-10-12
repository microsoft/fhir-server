// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
#if !Stu3 && !R4 && !R4B
using Microsoft.Health.Fhir.R5.Core.Extensions;
#endif
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Validates the complete SearchParameter set of a package as one unit and produces a plan that writes it.
    /// </summary>
    public sealed class SearchParameterPackageHandler : IFhirPackageResourceHandler
    {
        private const int MaxUrlLength = 128;

        private static readonly FhirJsonParser Parser = new FhirJsonParser();

        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator;
        private readonly ISearchParameterPackageWriter _writer;
        private readonly ILogger<SearchParameterPackageHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterPackageHandler"/> class.
        /// </summary>
        /// <param name="searchParameterDefinitionManager">The current SearchParameter definitions.</param>
        /// <param name="searchParameterOperations">Used to refresh the definitions before validation.</param>
        /// <param name="modelInfoProvider">The model info provider.</param>
        /// <param name="searchParameterComparer">The comparer used for code-conflict rules.</param>
        /// <param name="searchParameterSupportResolver">Decides whether the server can index a SearchParameter.</param>
        /// <param name="dataStoreSearchParameterValidator">Applies data-store-specific SearchParameter rules.</param>
        /// <param name="writer">Writes the validated set.</param>
        /// <param name="logger">The logger.</param>
        public SearchParameterPackageHandler(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterOperations searchParameterOperations,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IDataStoreSearchParameterValidator dataStoreSearchParameterValidator,
            ISearchParameterPackageWriter writer,
            ILogger<SearchParameterPackageHandler> logger)
        {
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _searchParameterComparer = EnsureArg.IsNotNull(searchParameterComparer, nameof(searchParameterComparer));
            _searchParameterSupportResolver = EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            _dataStoreSearchParameterValidator = EnsureArg.IsNotNull(dataStoreSearchParameterValidator, nameof(dataStoreSearchParameterValidator));
            _writer = EnsureArg.IsNotNull(writer, nameof(writer));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <inheritdoc />
        public string ResourceType => KnownResourceTypes.SearchParameter;

        /// <inheritdoc />
        public async Task<IFhirPackageInstallPlan> ValidateAsync(IReadOnlyList<FhirPackageEntry> entries, FhirPackage package, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(entries, nameof(entries));
            EnsureArg.IsNotNull(package, nameof(package));

            // Validate against the latest definitions, including ones added on other instances.
            await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);

            var issues = new List<OperationOutcomeIssue>();
            List<Candidate> candidates = ParseAndCheckStructure(entries, issues);

            CheckDuplicatesWithinPackage(candidates, issues);
            CheckAgainstExistingDefinitions(candidates, issues);
            CheckComponentsAndSupport(candidates, issues);

            if (issues.Count > 0)
            {
                _logger.LogWarning(
                    "SearchParameter set in FHIR package {PackageId} version {Version} failed validation with {IssueCount} issue(s).",
                    package.PackageId,
                    package.Version,
                    issues.Count);

                throw new FhirPackageException(issues);
            }

            return new SearchParameterPackageInstallPlan(
                package,
                candidates.Select(x => x.Resource).ToList(),
                candidates.Select(x => x.Url).ToList(),
                _writer);
        }

        private static List<Candidate> ParseAndCheckStructure(IReadOnlyList<FhirPackageEntry> entries, List<OperationOutcomeIssue> issues)
        {
            var candidates = new List<Candidate>();

            foreach (FhirPackageEntry entry in entries)
            {
                SearchParameter searchParameter;
                try
                {
                    searchParameter = Parser.Parse<SearchParameter>(entry.Json);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    issues.Add(Error(
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageEntryInvalid, entry.Path, KnownResourceTypes.SearchParameter, ex.Message)));
                    continue;
                }

                var candidate = new Candidate(entry, searchParameter);
                int issueCount = issues.Count;

                if (string.IsNullOrWhiteSpace(searchParameter.Url))
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, MissingElement("url"));
                }
                else if (searchParameter.Url.Length > MaxUrlLength || !Uri.TryCreate(searchParameter.Url, UriKind.Absolute, out _))
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Invalid, string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterInvalidUrl, MaxUrlLength));
                }

                if (string.IsNullOrWhiteSpace(searchParameter.Code))
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, MissingElement("code"));
                }

                if (candidate.BaseTypes.Count == 0)
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, MissingElement("base"));
                }

                if (searchParameter.Type == null)
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, MissingElement("type"));
                }

                if (string.IsNullOrWhiteSpace(searchParameter.Expression))
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, MissingElement("expression"));
                }

                if (searchParameter.Type == SearchParamType.Composite &&
                    (searchParameter.Component == null ||
                     searchParameter.Component.Count == 0 ||
                     searchParameter.Component.Any(c => c.GetComponentDefinitionUri() == null || string.IsNullOrWhiteSpace(c.Expression))))
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, Core.Resources.FhirPackageSearchParameterMissingComponent);
                }

                if (issues.Count == issueCount)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;

            static string MissingElement(string element) =>
                string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterMissingElement, element);
        }

        private static void CheckDuplicatesWithinPackage(List<Candidate> candidates, List<OperationOutcomeIssue> issues)
        {
            foreach (IGrouping<string, Candidate> group in candidates.GroupBy(x => x.Url, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                foreach (Candidate candidate in group)
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Duplicate, Core.Resources.FhirPackageSearchParameterDuplicateUrl);
                }
            }

            var codesByBase = candidates
                .SelectMany(c => c.BaseTypes.Select(b => (Base: b, c.SearchParameter.Code, Candidate: c)))
                .GroupBy(x => (x.Base, x.Code))
                .Where(g => g.Count() > 1);

            foreach (var group in codesByBase)
            {
                foreach (var item in group)
                {
                    AddIssue(
                        issues,
                        item.Candidate,
                        OperationOutcomeConstants.IssueType.Duplicate,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterDuplicateCode, group.Key.Code, group.Key.Base));
                }
            }
        }

        private void CheckAgainstExistingDefinitions(List<Candidate> candidates, List<OperationOutcomeIssue> issues)
        {
            foreach (Candidate candidate in candidates)
            {
                if (_searchParameterDefinitionManager.TryGetSearchParameter(candidate.Url, out SearchParameterInfo existing) && existing.IsSystemDefined)
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Conflict, Core.Resources.FhirPackageSearchParameterSystemDefinedUrl);
                    continue;
                }

                var failures = new List<ValidationFailure>();
                SearchParameterConflictRules.CheckForConflictingCodeValue(
                    candidate.SearchParameter,
                    _searchParameterDefinitionManager,
                    _modelInfoProvider,
                    _searchParameterComparer,
                    _logger,
                    failures);

                foreach (ValidationFailure failure in failures)
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Conflict, failure.ErrorMessage);
                }
            }
        }

        private void CheckComponentsAndSupport(List<Candidate> candidates, List<OperationOutcomeIssue> issues)
        {
            // Build every definition first so composite components can resolve to other parameters in the same package.
            var packageDefinitions = new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal);
            foreach (Candidate candidate in candidates)
            {
                candidate.Info = new SearchParameterInfo(new SearchParameterWrapper(candidate.Resource.Instance));
                packageDefinitions.TryAdd(candidate.Url, candidate.Info);
            }

            foreach (Candidate candidate in candidates)
            {
                if (!ResolveComponents(candidate, packageDefinitions, issues))
                {
                    continue;
                }

                (bool Supported, bool IsPartiallySupported) support;
                try
                {
                    support = _searchParameterSupportResolver.IsSearchParameterSupported(candidate.Info);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterInvalidExpression, ex.Message));
                    continue;
                }

                if (!support.Supported)
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.NotSupported,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            Core.Resources.FhirPackageSearchParameterNotSupported,
                            string.Format(CultureInfo.InvariantCulture, Core.Resources.NoConverterForSearchParamType, candidate.Info.Type, candidate.Info.Expression)));
                }
                else if (support.IsPartiallySupported)
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.NotSupported, Core.Resources.FhirPackageSearchParameterPartiallySupported);
                }
                else if (!_dataStoreSearchParameterValidator.ValidateSearchParameter(candidate.Info, out string errorMessage))
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.NotSupported,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterNotSupported, errorMessage));
                }
            }
        }

        private bool ResolveComponents(Candidate candidate, Dictionary<string, SearchParameterInfo> packageDefinitions, List<OperationOutcomeIssue> issues)
        {
            bool resolved = true;
            IReadOnlyList<SearchParameterComponentInfo> components = candidate.Info.Component ?? Array.Empty<SearchParameterComponentInfo>();

            for (int i = 0; i < components.Count; i++)
            {
                string definitionUrl = components[i].DefinitionUrl.OriginalString;

                // Same-package definitions take precedence; otherwise any current definition (core or existing) may be used.
                if (!packageDefinitions.TryGetValue(definitionUrl, out SearchParameterInfo target) &&
                    !_searchParameterDefinitionManager.TryGetSearchParameter(definitionUrl, true, out target))
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterUnresolvedComponent, i, definitionUrl));
                    resolved = false;
                    continue;
                }

                if (target.Type == ValueSets.SearchParamType.Composite)
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterCompositeComponent, i, definitionUrl));
                    resolved = false;
                    continue;
                }

                components[i].ResolvedSearchParameter = target;
            }

            return resolved;
        }

        private static void AddIssue(List<OperationOutcomeIssue> issues, Candidate candidate, string issueType, string detail)
        {
            issues.Add(Error(
                issueType,
                string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterIssue, candidate.Entry.Path, candidate.Url, detail)));
        }

        private static OperationOutcomeIssue Error(string issueType, string diagnostics) =>
            new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Error, issueType, diagnostics);

        private sealed class Candidate
        {
            public Candidate(FhirPackageEntry entry, SearchParameter searchParameter)
            {
                Entry = entry;
                SearchParameter = searchParameter;
                Resource = searchParameter.ToResourceElement();
                BaseTypes = searchParameter.Base?.Where(x => x != null).Select(x => x.ToString()).ToList() ?? new List<string>();
            }

            public FhirPackageEntry Entry { get; }

            public SearchParameter SearchParameter { get; }

            public ResourceElement Resource { get; }

            public string Url => SearchParameter.Url;

            public List<string> BaseTypes { get; }

            public SearchParameterInfo Info { get; set; }
        }

        private sealed class SearchParameterPackageInstallPlan : IFhirPackageInstallPlan
        {
            private readonly FhirPackage _package;
            private readonly IReadOnlyList<ResourceElement> _searchParameters;
            private readonly ISearchParameterPackageWriter _writer;

            public SearchParameterPackageInstallPlan(FhirPackage package, IReadOnlyList<ResourceElement> searchParameters, IReadOnlyList<string> canonicalUrls, ISearchParameterPackageWriter writer)
            {
                _package = package;
                _searchParameters = searchParameters;
                _writer = writer;
                CanonicalUrls = canonicalUrls;
            }

            public string ResourceType => KnownResourceTypes.SearchParameter;

            public IReadOnlyList<string> CanonicalUrls { get; }

            public Task ApplyAsync(CancellationToken cancellationToken) => _writer.WriteAsync(_package, _searchParameters, cancellationToken);
        }
    }
}
