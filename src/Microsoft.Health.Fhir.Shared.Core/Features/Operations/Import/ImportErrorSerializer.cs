// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Validation;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Operations.Import
{
    public class ImportErrorSerializer : IImportErrorSerializer
    {
        private readonly FhirJsonSerializer _fhirJsonSerializer;

        public ImportErrorSerializer(FhirJsonSerializer fhirJsonSerializer)
        {
            EnsureArg.IsNotNull(fhirJsonSerializer, nameof(fhirJsonSerializer));

            _fhirJsonSerializer = fhirJsonSerializer;
        }

        public string Serialize(long index, Exception ex, long offset)
        {
            EnsureArg.IsNotNull(ex, nameof(ex));

            // A validation failure carries its reasons in the issues; its message is only a generic summary.
            string errorMessage = ex is ResourceNotValidException { Issues.Count: > 0 } notValid
                ? string.Join(" ", notValid.Issues.Select(issue => issue.Diagnostics))
                : ex.Message;

            return Serialize(index, errorMessage, offset);
        }

        public string Serialize(long index, string errorMessage, long offset)
        {
            EnsureArg.IsNotNullOrEmpty(errorMessage, nameof(errorMessage));

            var issue = new OperationOutcome.IssueComponent();
            issue.Severity = OperationOutcome.IssueSeverity.Error;
            issue.Diagnostics = string.Format("Failed to process resource at line: {0} with stream start offset: {1}", index, offset);
            issue.Details = new CodeableConcept();
            issue.Details.Text = errorMessage;
            OperationOutcome operationOutcome = new OperationOutcome();
            operationOutcome.Issue.Add(issue);

            return _fhirJsonSerializer.SerializeToString(operationOutcome);
        }
    }
}
