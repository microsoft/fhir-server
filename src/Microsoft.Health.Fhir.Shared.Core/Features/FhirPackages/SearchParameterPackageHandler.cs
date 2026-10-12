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
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
#if !Stu3 && !R4 && !R4B
using Microsoft.Health.Fhir.R5.Core.Extensions;
#endif
using Task = System.Threading.Tasks.Task;
using ValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Validates the complete SearchParameter set of a package as one unit and produces a plan that writes it.
    /// </summary>
    public sealed class SearchParameterPackageHandler : IFhirPackageResourceHandler
    {
        private const int MaxUrlLength = 128;

        private static readonly HashSet<SearchParameterStatus> EligibleExternalComponentStatuses = new HashSet<SearchParameterStatus>
        {
            SearchParameterStatus.Enabled,
            SearchParameterStatus.Supported,
        };

        private static readonly FhirJsonParser Parser = new FhirJsonParser();

        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator;
        private readonly IModelAttributeValidator _modelAttributeValidator;
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
        /// <param name="modelAttributeValidator">Validates each resource against the FHIR model, including cardinality.</param>
        /// <param name="writer">Writes the validated set.</param>
        /// <param name="logger">The logger.</param>
        public SearchParameterPackageHandler(
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterOperations searchParameterOperations,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IDataStoreSearchParameterValidator dataStoreSearchParameterValidator,
            IModelAttributeValidator modelAttributeValidator,
            ISearchParameterPackageWriter writer,
            ILogger<SearchParameterPackageHandler> logger)
        {
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _searchParameterComparer = EnsureArg.IsNotNull(searchParameterComparer, nameof(searchParameterComparer));
            _searchParameterSupportResolver = EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            _dataStoreSearchParameterValidator = EnsureArg.IsNotNull(dataStoreSearchParameterValidator, nameof(dataStoreSearchParameterValidator));
            _modelAttributeValidator = EnsureArg.IsNotNull(modelAttributeValidator, nameof(modelAttributeValidator));
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

            // Validate against the latest definitions, including ones added on other instances. The writer uses the
            // resulting reference-set version to detect changes made between validation and persistence.
            await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
            DateTimeOffset referenceSetLastUpdated = _searchParameterOperations.SearchParamLastUpdated;

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
                referenceSetLastUpdated,
                _writer);
        }

        private List<Candidate> ParseAndCheckStructure(IReadOnlyList<FhirPackageEntry> entries, List<OperationOutcomeIssue> issues)
        {
            var candidates = new List<Candidate>();

            foreach (FhirPackageEntry entry in entries)
            {
                SearchParameter searchParameter;
                try
                {
                    searchParameter = Parser.Parse<SearchParameter>(entry.Json);
                }
                catch (FormatException ex)
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

                CheckComponentStructure(candidate, issues);

                // Model (cardinality) validation catches required elements the indexing checks above don't cover,
                // such as name and status. It runs only when those checks pass, so one problem isn't reported twice.
                if (issues.Count == issueCount)
                {
                    var validationResults = new List<ValidationResult>();
                    if (!_modelAttributeValidator.TryValidate(candidate.Resource, validationResults, recurse: true))
                    {
                        foreach (ValidationResult result in validationResults)
                        {
                            string members = string.Join(", ", result.MemberNames);
                            AddIssue(
                                issues,
                                candidate,
                                OperationOutcomeConstants.IssueType.Invalid,
                                string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterModelInvalid, string.IsNullOrEmpty(members) ? result.ErrorMessage : $"{members}: {result.ErrorMessage}"));
                        }
                    }
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

        /// <summary>
        /// Establishes the component contract before anything constructs a <see cref="Uri"/> from a component definition:
        /// a composite has at least one component, and every component has an absolute definition URI and an expression.
        /// </summary>
        private static void CheckComponentStructure(Candidate candidate, List<OperationOutcomeIssue> issues)
        {
            List<SearchParameter.ComponentComponent> components = candidate.SearchParameter.Component;

            if (candidate.SearchParameter.Type == SearchParamType.Composite && (components == null || components.Count == 0))
            {
                AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, Core.Resources.FhirPackageSearchParameterMissingComponent);
                return;
            }

            for (int i = 0; i < (components?.Count ?? 0); i++)
            {
                string definition = GetComponentDefinitionText(components[i]);

                if (string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(components[i].Expression))
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Required, Core.Resources.FhirPackageSearchParameterMissingComponent);
                }
                else if (!Uri.TryCreate(definition, UriKind.Absolute, out _))
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterInvalidComponentDefinition, i, definition));
                }
            }
        }

        private static string GetComponentDefinitionText(SearchParameter.ComponentComponent component)
        {
#if Stu3
            return component.Definition?.Reference;
#else
            return component.Definition;
#endif
        }

        private void CheckDuplicatesWithinPackage(List<Candidate> candidates, List<OperationOutcomeIssue> issues)
        {
            foreach (IGrouping<string, Candidate> group in candidates.GroupBy(x => x.Url, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                foreach (Candidate candidate in group)
                {
                    AddIssue(issues, candidate, OperationOutcomeConstants.IssueType.Duplicate, Core.Resources.FhirPackageSearchParameterDuplicateUrl);
                }
            }

            // Compare codes on effective resource types, so Resource or DomainResource members collide with
            // members on the concrete types that inherit them, exactly as they would at search time.
            var effectiveBaseCache = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
            var membersByTypeAndCode = new Dictionary<(string Type, string Code), List<Candidate>>();
            foreach (Candidate candidate in candidates)
            {
                foreach (string baseType in candidate.BaseTypes)
                {
                    if (!effectiveBaseCache.TryGetValue(baseType, out IReadOnlyCollection<string> effectiveTypes))
                    {
                        effectiveTypes = GetEffectiveBaseTypes(baseType);
                        effectiveBaseCache.Add(baseType, effectiveTypes);
                    }

                    foreach (string effectiveType in effectiveTypes)
                    {
                        if (!membersByTypeAndCode.TryGetValue((effectiveType, candidate.SearchParameter.Code), out List<Candidate> members))
                        {
                            members = new List<Candidate>();
                            membersByTypeAndCode.Add((effectiveType, candidate.SearchParameter.Code), members);
                        }

                        if (!members.Contains(candidate))
                        {
                            members.Add(candidate);
                        }
                    }
                }
            }

            var reported = new HashSet<(Candidate, string)>();
            foreach (KeyValuePair<(string Type, string Code), List<Candidate>> collision in membersByTypeAndCode
                .Where(x => x.Value.Count > 1)
                .OrderBy(x => x.Key.Type, StringComparer.Ordinal))
            {
                foreach (Candidate candidate in collision.Value)
                {
                    if (reported.Add((candidate, collision.Key.Code)))
                    {
                        AddIssue(
                            issues,
                            candidate,
                            OperationOutcomeConstants.IssueType.Duplicate,
                            string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterDuplicateCode, collision.Key.Code, collision.Key.Type));
                    }
                }
            }
        }

        private IReadOnlyCollection<string> GetEffectiveBaseTypes(string baseType)
        {
            if (string.Equals(baseType, KnownResourceTypes.Resource, StringComparison.OrdinalIgnoreCase))
            {
                return _modelInfoProvider.GetResourceTypeNames();
            }

            if (string.Equals(baseType, KnownResourceTypes.DomainResource, StringComparison.Ordinal))
            {
                return _modelInfoProvider.GetResourceTypeNames()
                    .Where(x => _modelInfoProvider.GetFhirTypeNameForType(_modelInfoProvider.GetTypeForFhirType(x).BaseType) == KnownResourceTypes.DomainResource)
                    .ToList();
            }

            return new[] { baseType };
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
                catch (Exception ex) when (ex is FormatException or NotSupportedException)
                {
                    // FHIRPath syntax errors in the expression or a component expression, or no resolvable target types.
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
                bool inPackage = packageDefinitions.TryGetValue(definitionUrl, out SearchParameterInfo target);
                if (!inPackage && !_searchParameterDefinitionManager.TryGetSearchParameter(definitionUrl, out target))
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterUnresolvedComponent, i, definitionUrl));
                    resolved = false;
                    continue;
                }

                // A component outside the package must be a live definition; retired or disabled ones would leave the composite unusable.
                if (!inPackage && !EligibleExternalComponentStatuses.Contains(target.SearchParameterStatus))
                {
                    AddIssue(
                        issues,
                        candidate,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(CultureInfo.InvariantCulture, Core.Resources.FhirPackageSearchParameterIneligibleComponent, i, definitionUrl, target.SearchParameterStatus));
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
            private readonly DateTimeOffset _referenceSetLastUpdated;
            private readonly ISearchParameterPackageWriter _writer;

            public SearchParameterPackageInstallPlan(
                FhirPackage package,
                IReadOnlyList<ResourceElement> searchParameters,
                IReadOnlyList<string> canonicalUrls,
                DateTimeOffset referenceSetLastUpdated,
                ISearchParameterPackageWriter writer)
            {
                _package = package;
                _searchParameters = searchParameters;
                _referenceSetLastUpdated = referenceSetLastUpdated;
                _writer = writer;
                CanonicalUrls = canonicalUrls;
            }

            public string ResourceType => KnownResourceTypes.SearchParameter;

            public IReadOnlyList<string> CanonicalUrls { get; }

            public Task ApplyAsync(CancellationToken cancellationToken) =>
                _writer.WriteAsync(_package, _searchParameters, _referenceSetLastUpdated, cancellationToken);
        }
    }
}
