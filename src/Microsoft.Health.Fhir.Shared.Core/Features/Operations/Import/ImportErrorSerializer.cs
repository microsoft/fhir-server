// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;

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

        public string Serialize(ProcessError error)
        {
            var issue = new OperationOutcome.IssueComponent();
            issue.Severity = OperationOutcome.IssueSeverity.Error;
            issue.Diagnostics = $"Failed to process resource at line: {error.LineNumber}";
            issue.Details.Text = error.ErrorMessage;
            OperationOutcome operationOutcome = new OperationOutcome();
            operationOutcome.Issue.Add(issue);

            return _fhirJsonSerializer.SerializeToString(operationOutcome);
        }
    }
}
