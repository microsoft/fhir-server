// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class RawResourceExtensions
    {
        /// <summary>
        /// Converts the RawResource to an ITypedElement
        /// </summary>
        /// <param name="rawResource">The RawResource to convert</param>
        /// <param name="modelInfoProvider">The IModelInfoProvider to use when converting the RawResource</param>
        /// <returns>An ITypedElement of the RawResource</returns>
        public static ITypedElement ToITypedElement(this RawResource rawResource, IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(rawResource, nameof(rawResource));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            using TextReader reader = new StringReader(rawResource.Data);
            using JsonReader jsonReader = new JsonTextReader(reader);
            try
            {
                ISourceNode sourceNode = FhirJsonNode.Read(jsonReader);
                return modelInfoProvider.ToTypedElement(sourceNode);
            }
            catch (FormatException ex)
            {
                var issue = new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Fatal,
                    OperationOutcomeConstants.IssueType.Invalid,
                    ex.Message);

                throw new InvalidDefinitionException(
                    Core.Resources.SearchParameterDefinitionContainsInvalidEntry,
                    new OperationOutcomeIssue[] { issue });
            }
        }
    }
}
