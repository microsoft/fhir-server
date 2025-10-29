// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Hl7.Fhir.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ProfileValidator : IProfileValidator
    {
        private readonly TimeSpan _validatatorRefresh = TimeSpan.FromMinutes(30);
        private readonly IResourceResolver _resolver;
        private readonly ILogger<ProfileValidator> _logger;
        private readonly int _maxExpansionSize;

        private Validator _validator;
        private DateTime _lastValidatorRefresh = DateTime.MinValue;

        public ProfileValidator(
            IProvideProfilesForValidation profilesResolver,
            IOptions<ValidateOperationConfiguration> options,
            ILogger<ProfileValidator> logger)
        {
            EnsureArg.IsNotNull(profilesResolver, nameof(profilesResolver));
            EnsureArg.IsNotNull(options?.Value, nameof(options));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;

            try
            {
                int cacheDuration = options.Value.CacheDurationInSeconds <= 0 ? ValidateOperationConfiguration.DefaultCacheDurationInSeconds : options.Value.CacheDurationInSeconds;
                _maxExpansionSize = options.Value.MaxExpansionSize <= 0 ? ValidateOperationConfiguration.DefaultMaxExpansionSize : options.Value.MaxExpansionSize;

                _logger.LogInformation(
                    "Creating ProfileValidator with: CacheDuration {CacheDurationInSeconds}; and MaxExpansionSize {MaxExpansionSize}.",
                    cacheDuration,
                    _maxExpansionSize);

                _resolver = new MultiResolver(new CachedResolver(ZipSource.CreateValidationSource(), cacheDuration), profilesResolver);
            }
            catch (Exception)
            {
                // Something went wrong during profile loading, what should we do?
                throw;
            }
        }

        private Validator GetValidator()
        {
            if (_validator != null && (DateTime.UtcNow - _lastValidatorRefresh) < _validatatorRefresh)
            {
                return _validator;
            }

            _logger.LogInformation("Refreshing validator");
            _lastValidatorRefresh = DateTime.UtcNow;

            var expanderSettings = new ValueSetExpanderSettings
            {
                MaxExpansionSize = _maxExpansionSize, // Set your desired max expansion size here
            };

            var terminologyService = new LocalTerminologyService(_resolver.AsAsync(), expanderSettings);

            var ctx = new ValidationSettings()
            {
                ResourceResolver = _resolver,
                GenerateSnapshot = true,
                Trace = false,
                ResolveExternalReferences = false,
                TerminologyService = terminologyService,
            };

            _validator = new Validator(ctx);

            return _validator;
        }

        public OperationOutcomeIssue[] TryValidate(ITypedElement resource, string profile = null)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            _logger.LogDebug("Getting Validator");
            var validator = GetValidator();
            OperationOutcome result;

            _logger.LogDebug("Validating");
            if (!string.IsNullOrWhiteSpace(profile))
            {
                result = validator.Validate(resource, profile);
            }
            else
            {
                result = validator.Validate(resource);
            }

            _logger.LogDebug("Finished validating");

            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning("Long running validation: {Time}", stopwatch.ElapsedMilliseconds);
            }

            var outcomeIssues = result.Issue.OrderBy(x => x.Severity)
                .Select(issue =>
                    new OperationOutcomeIssue(
                        issue.Severity?.ToString(),
                        issue.Code.ToString(),
                        diagnostics: issue.Diagnostics,
                        detailsText: issue.Details?.Text,
                        detailsCodes: issue.Details?.Coding != null ? new CodableConceptInfo(issue.Details.Coding.Select(x => new Coding(x.System, x.Code, x.Display))) : null,
                        expression: issue.Expression.ToArray()))
                .ToArray();

            return outcomeIssues;
        }
    }
}
