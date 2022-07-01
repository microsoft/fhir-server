// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Hl7.Fhir.Validation;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public sealed class ProfileValidator : IProfileValidator, IDisposable
    {
        private readonly IResourceResolver _resolver;
        private readonly BaseFhirClient _client;
        private readonly FallbackTerminologyService _ts = null;

        public ProfileValidator(IProvideProfilesForValidation profilesResolver, IOptions<ValidateOperationConfiguration> options)
        {
            EnsureArg.IsNotNull(profilesResolver, nameof(profilesResolver));
            EnsureArg.IsNotNull(options?.Value, nameof(options));

            try
            {
                ZipSource zipSource = ZipSource.CreateValidationSource(@"definitions.zip");

                // Force summaries to be loaded.
                zipSource.ListSummaries();

                _resolver = new MultiResolver(new CachedResolver(zipSource, options.Value.CacheDurationInSeconds), profilesResolver);

                if (!string.IsNullOrEmpty(options.Value.ExternalTerminologyServer))
                {
                    var settings = new FhirClientSettings
                    {
                        Timeout = 300000,
                        PreferredFormat = ResourceFormat.Json,
                        VerifyFhirVersion = true,
                        PreferredReturn = Prefer.ReturnRepresentation,
                    };

                    if (!string.IsNullOrEmpty(options.Value.ApiKey))
                    {
#pragma warning disable CA2000 // Dispose objects before losing scope
                        var handler = new AuthorizationMessageHandler();
#pragma warning restore CA2000 // Dispose objects before losing scope
                        string encodedKey = Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes("apikey" + ":" + options.Value.ApiKey));
                        handler.Authorization = new AuthenticationHeaderValue("Basic", encodedKey);
                        _client = new FhirClient(options.Value.ExternalTerminologyServer, settings, handler);
                    }
                    else
                    {
                        _client = new FhirClient(options.Value.ExternalTerminologyServer, settings);
                    }

                    _ts = new FallbackTerminologyService(new LocalTerminologyService(_resolver.AsAsync(), new ValueSetExpanderSettings() { ValueSetSource = _resolver }), new ExternalTerminologyService(_client));
                }
            }
            catch (Exception)
            {
                // Something went wrong during profile loading, what should we do?
                throw;
            }
        }

        private Validator GetValidator()
        {
            var ctx = new ValidationSettings()
            {
                ResourceResolver = _resolver,
                GenerateSnapshot = true,
                Trace = false,
                ResolveExternalReferences = false,
                TerminologyService = _ts,
            };

            var validator = new Validator(ctx);

            return validator;
        }

        public OperationOutcomeIssue[] TryValidate(ITypedElement resource, string profile = null)
        {
            var validator = GetValidator();
            OperationOutcome result;
            if (!string.IsNullOrWhiteSpace(profile))
            {
                result = validator.Validate(resource, profile);
            }
            else
            {
                result = validator.Validate(resource);
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

#pragma warning disable CA1063 // Implement IDisposable Correctly
        public void Dispose()
#pragma warning restore CA1063 // Implement IDisposable Correctly
        {
            if (_client != null)
            {
                _client.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
