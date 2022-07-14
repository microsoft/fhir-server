// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Terminology;
using Hl7.Fhir.Validation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public sealed class ProfileValidator : IProfileValidator // , IDisposable
    {
        private readonly FallbackTerminologyService _ts = null;
        private readonly Validator _validator = null;

        public ProfileValidator(FallbackTerminologyService tsResolver, Validator validatorResolver)
        {
            _ts = tsResolver;
            _validator = validatorResolver;
        }

        public OperationOutcomeIssue[] TryValidate(ITypedElement resource, string profile = null)
        {
            OperationOutcome result;
            if (!string.IsNullOrWhiteSpace(profile))
            {
                result = _validator.Validate(resource, profile);
            }
            else
            {
                result = _validator.Validate(resource);
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
        /* public void Dispose()
#pragma warning restore CA1063 // Implement IDisposable Correctly
        {
            if (_client != null)
            {
                _client.Dispose();
            }

            GC.SuppressFinalize(this);
        }*/
    }
}
