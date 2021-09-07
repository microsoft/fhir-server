// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Validation;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ResourceValidator : IResourceValidator
    {
        private readonly Validator _validator;

        public ResourceValidator()
        {
            var resolver = new CachedResolver(ZipSource.CreateValidationSource());
            var ctx = new ValidationSettings()
            {
                ResourceResolver = resolver,
                GenerateSnapshot = true,
                Trace = false,
                ResolveExternalReferences = false,
            };

            _validator = new Validator(ctx);
        }

        public OperationOutcomeIssue[] TryValidate(ITypedElement resource)
        {
            var result = _validator.Validate(resource);
            var outcomeIssues = result.Issue.OrderBy(x => x.Severity)
               .Select(issue =>
                   new OperationOutcomeIssue(
                       issue.Severity?.ToString(),
                       issue.Code.ToString(),
                       diagnostics: issue.Diagnostics,
                       detailsText: issue.Details?.Text,
                       detailsCodes: issue.Details?.Coding != null ? new CodableConceptInfo(issue.Details.Coding.Select(x => new Coding(x.System, x.Code, x.Display))) : null,
                       location: issue.Location.ToArray()))
               .ToArray();
            return outcomeIssues;
        }
    }
}
