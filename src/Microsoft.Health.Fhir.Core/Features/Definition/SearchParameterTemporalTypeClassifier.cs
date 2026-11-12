// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Specification;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    internal static class SearchParameterTemporalTypeClassifier
    {
        public static TemporalType Classify(SearchParameterInfo parameter, IModelInfoProvider modelInfoProvider)
        {
            if (parameter.Type != SearchParamType.Date ||
                parameter.Component?.Count > 0 ||
                string.IsNullOrEmpty(parameter.Expression))
            {
                return TemporalType.Unknown;
            }

            string[] branches = parameter.Expression.Split('|');
            TemporalType result = TemporalType.Unknown;
            foreach (string branch in branches)
            {
                // Preserve exact single-path syntax; whitespace is only a union separator.
                TemporalType type = ClassifyPath(branches.Length == 1 ? branch : branch.Trim(), modelInfoProvider);
                if (type == TemporalType.Unknown)
                {
                    return TemporalType.Unknown;
                }

                result = result == TemporalType.Unknown ? type : result == type ? result : TemporalType.Mixed;
            }

            return result;
        }

        private static TemporalType ClassifyPath(string expression, IModelInfoProvider modelInfoProvider)
        {
            string[] path = expression.Split('.');
            if (path.Length < 2 || path.Any(segment =>
                segment.Length == 0 || !char.IsAsciiLetter(segment[0]) ||
                segment.Any(character => !char.IsAsciiLetterOrDigit(character))) ||
                !modelInfoProvider.IsKnownResource(path[0], ignoreCase: false))
            {
                return TemporalType.Unknown;
            }

            IStructureDefinitionSummary summary = modelInfoProvider.StructureDefinitionSummaryProvider.Provide(path[0]);
            for (int i = 1; i < path.Length; i++)
            {
                if (summary == null)
                {
                    return TemporalType.Unknown;
                }

                IElementDefinitionSummary[] matches = summary.GetElements()
                    .Where(element => string.Equals(element.ElementName, path[i], StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                if (matches.Length != 1 || matches[0].IsChoiceElement || matches[0].Type?.Length != 1)
                {
                    return TemporalType.Unknown;
                }

                ITypeSerializationInfo type = matches[0].Type[0];
                if (i == path.Length - 1)
                {
                    return type is IStructureDefinitionReference reference ? reference.ReferredType switch
                    {
                        "date" => TemporalType.Date,
                        "dateTime" => TemporalType.DateTime,
                        "instant" => TemporalType.Instant,
                        "Period" => TemporalType.Period,
                        "Timing" => TemporalType.Timing,
                        _ => TemporalType.Unknown,
                    }
                    : TemporalType.Unknown;
                }

                summary = type switch
                {
                    IStructureDefinitionSummary inlineSummary => inlineSummary,
                    IStructureDefinitionReference reference when !string.IsNullOrEmpty(reference.ReferredType) =>
                        modelInfoProvider.StructureDefinitionSummaryProvider.Provide(reference.ReferredType),
                    _ => null,
                };
            }

            return TemporalType.Unknown;
        }
    }
}
