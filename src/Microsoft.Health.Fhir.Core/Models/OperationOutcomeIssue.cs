// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class OperationOutcomeIssue
    {
        public OperationOutcomeIssue(
             string severity,
             string code,
             string diagnostics = null,
             CodableConceptInfo detailsCodes = null,
             string detailsText = null,
             string[] expression = null)
        {
            EnsureArg.IsNotNullOrEmpty(severity, nameof(severity));
            EnsureArg.IsNotNullOrEmpty(code, nameof(code));

            Severity = severity;
            Code = code;
            DetailsCodes = detailsCodes;

            if (expression != null)
            {
                if (DetailsCodes == null)
                {
                    DetailsCodes = new CodableConceptInfo();
                }

                DetailsCodes.Coding.Add(new Hl7.Fhir.Model.Coding(Resources.Meta, Resources.Meta, Resources.OperationOutcomeLocationDeprication));
            }

            DetailsText = detailsText;
            Diagnostics = diagnostics;
#pragma warning disable CS0618 // Type or member is obsolete
            Location = expression;
#pragma warning restore CS0618 // Type or member is obsolete
            Expression = expression;
        }

        public string Severity { get; }

        public string Code { get; }

        public CodableConceptInfo DetailsCodes { get; }

        public string DetailsText { get; }

        public string Diagnostics { get; }

        /// <summary>
        /// Deprecated value is still being used for backwards compatibility until users have had time to switch.
        /// </summary>
        [Obsolete("Location is deprecated, please use Expression instead.")]
        public ICollection<string> Location { get; }

        public ICollection<string> Expression { get; }
    }
}
