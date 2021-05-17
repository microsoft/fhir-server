// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal class SqlServerSortingValidator : ISortingValidator
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly HashSet<Uri> _supportedParameterUrisBeforeTablePartitioning;
        private readonly HashSet<Uri> _supportedParameterUrisAfterTablePartitioning;

        public SqlServerSortingValidator(SchemaInformation schemaInformation)
        {
            _schemaInformation = schemaInformation;
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            _supportedParameterUrisBeforeTablePartitioning = new HashSet<Uri>()
            {
                SearchParameterNames.LastUpdatedUri,
                new Uri("http://hl7.org/fhir/SearchParameter/individual-birthdate"),
                new Uri("http://hl7.org/fhir/SearchParameter/clinical-date"),
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-abatement-date"),
                new Uri("http://hl7.org/fhir/SearchParameter/Condition-onset-date"),
                new Uri("http://hl7.org/fhir/SearchParameter/DiagnosticReport-issued"),
                new Uri("http://hl7.org/fhir/SearchParameter/Claim-created"),
                new Uri("http://hl7.org/fhir/SearchParameter/ClaimResponse-created"),
                new Uri("http://hl7.org/fhir/SearchParameter/DocumentManifest-created"),
                new Uri("http://hl7.org/fhir/SearchParameter/ExplanationOfBenefit-created"),
                new Uri("http://hl7.org/fhir/SearchParameter/Media-created"),
                new Uri("http://hl7.org/fhir/SearchParameter/PaymentNotice-created"),
                new Uri("http://hl7.org/fhir/SearchParameter/PaymentReconciliation-created"),
                new Uri("http://hl7.org/fhir/SearchParameter/ImagingStudy-started"),
                new Uri("http://hl7.org/fhir/SearchParameter/MedicationRequest-authoredon"),
            };

            _supportedParameterUrisAfterTablePartitioning = new HashSet<Uri>(_supportedParameterUrisBeforeTablePartitioning)
            {
                SearchParameterNames.ResourceTypeUri,
            };
        }

        internal HashSet<Uri> SupportedParameterUris => _schemaInformation.Current < SchemaVersionConstants.PartitionedTables ? _supportedParameterUrisBeforeTablePartitioning : _supportedParameterUrisAfterTablePartitioning;

        public bool ValidateSorting(IReadOnlyList<(SearchParameterInfo searchParameter, SortOrder sortOrder)> sorting, out IReadOnlyList<string> errorMessages)
        {
            EnsureArg.IsNotNull(sorting, nameof(sorting));

            switch (sorting)
            {
                case { Count: 0 }:
                case { Count: 1 } when SupportedParameterUris.Contains(sorting[0].searchParameter.Url):
                    errorMessages = Array.Empty<string>();
                    return true;
                case { Count: 1 }:
                    errorMessages = new[] { string.Format(CultureInfo.InvariantCulture, Core.Resources.SearchSortParameterNotSupported, sorting[0].searchParameter.Code) };
                    return false;
                case { Count: 2 } when _schemaInformation.Current >= SchemaVersionConstants.PartitionedTables &&
                                       sorting[0].searchParameter.Url.Equals(SearchParameterNames.ResourceTypeUri) &&
                                       sorting[1].searchParameter.Url.Equals(SearchParameterNames.LastUpdatedUri):

                    if (sorting[0].sortOrder != sorting[1].sortOrder)
                    {
                        errorMessages = new[] { Resources.TypeAndLastUpdatedMustHaveSameSortDirection };
                        return false;
                    }

                    errorMessages = Array.Empty<string>();
                    return true;
                default:
                    errorMessages = new[] { Resources.OnlyTypeAndLastUpdatedSupportedForCompoundSort };
                    return false;
            }
        }
    }
}
