// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    public class FirelyTerminologyServiceProxy : ITerminologyServiceProxy
    {
        private static readonly Dictionary<string, Type> ExpandParameterTypeMap = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { TerminologyOperationParameterNames.Expand.Url, typeof(FhirUri) },
            { TerminologyOperationParameterNames.Expand.ValueSet, typeof(ValueSet) },
            { TerminologyOperationParameterNames.Expand.ValueSetVersion, typeof(FhirString) },
            { TerminologyOperationParameterNames.Expand.Context, typeof(FhirUri) },
            { TerminologyOperationParameterNames.Expand.ContextDirection, typeof(Code) },
            { TerminologyOperationParameterNames.Expand.Filter, typeof(FhirString) },
            { TerminologyOperationParameterNames.Expand.Date, typeof(FhirDateTime) },
            { TerminologyOperationParameterNames.Expand.Offset, typeof(Integer) },
            { TerminologyOperationParameterNames.Expand.Count, typeof(Integer) },
            { TerminologyOperationParameterNames.Expand.IncludeDesignations, typeof(FhirBoolean) },
            { TerminologyOperationParameterNames.Expand.Designation, typeof(FhirString) },
            { TerminologyOperationParameterNames.Expand.IncludeDefinition, typeof(FhirBoolean) },
            { TerminologyOperationParameterNames.Expand.ActiveOnly, typeof(FhirBoolean) },
            { TerminologyOperationParameterNames.Expand.ExcludeNested, typeof(FhirBoolean) },
            { TerminologyOperationParameterNames.Expand.ExcludeNotForUI, typeof(FhirBoolean) },
            { TerminologyOperationParameterNames.Expand.ExcludePostCoordinated, typeof(FhirBoolean) },
            { TerminologyOperationParameterNames.Expand.DisplayLanguage, typeof(Code) },
            { TerminologyOperationParameterNames.Expand.ExcludeSystem, typeof(Canonical) },
            { TerminologyOperationParameterNames.Expand.SystemVersion, typeof(Canonical) },
            { TerminologyOperationParameterNames.Expand.CheckSystemVersion, typeof(Canonical) },
            { TerminologyOperationParameterNames.Expand.ForceSystemVersion, typeof(Canonical) },
        };

        private readonly FhirJsonParser _parser;
        private readonly ITerminologyService _terminologyService;
        private readonly IAsyncResourceResolver _resourceResolver;
        private readonly ILogger<FirelyTerminologyServiceProxy> _logger;

        public FirelyTerminologyServiceProxy(
            ITerminologyService terminologyService,
            IAsyncResourceResolver resourceResolver,
            ILogger<FirelyTerminologyServiceProxy> logger)
        {
            EnsureArg.IsNotNull(terminologyService, nameof(terminologyService));
            EnsureArg.IsNotNull(resourceResolver, nameof(resourceResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _parser = new FhirJsonParser(
                new ParserSettings()
                {
                    PermissiveParsing = false,
                });
            _terminologyService = terminologyService;
            _resourceResolver = resourceResolver;
            _logger = logger;
        }

        public async Task<ResourceElement> ExpandAsync(
            IReadOnlyList<Tuple<string, string>> parameters,
            string resourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                parameters = await ProcessExpandParameters(parameters);
                var resource = await _terminologyService.Expand(
                    CreateExpandParameters(parameters),
                    resourceId);
#if R4B || R4
                if (resource is ValueSet)
                {
                    // NOTE: this process is needed for R4 and R4B to remove properties added by the expander due to the bug
                    // that are not compliant to a specifc FHIR version. (Bug: https://github.com/FirelyTeam/firely-net-sdk/issues/3327)
                    resource = ProcessExpandedValueSet((ValueSet)resource);
                }
#endif
                return resource.ToResourceElement();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to expand.");
                return CreateOperationOutcome(ex);
            }
        }

        private Parameters CreateExpandParameters(IReadOnlyList<Tuple<string, string>> parameterList)
        {
            var parameters = new Parameters();
            if (parameterList != null)
            {
                foreach (var p in parameterList)
                {
                    if (p == null)
                    {
                        continue;
                    }

                    parameters.Parameter.Add(CreateExpandParameterComponent(p));
                }
            }

            return parameters;
        }

        private Parameters.ParameterComponent CreateExpandParameterComponent(Tuple<string, string> parameter)
        {
            if (parameter == null)
            {
                return new Parameters.ParameterComponent();
            }

            var type = typeof(FhirString);
            if (ExpandParameterTypeMap.TryGetValue(parameter.Item1, out var typeMapped))
            {
                type = typeMapped;
            }

            var isResource = string.Equals(parameter?.Item1, TerminologyOperationParameterNames.Expand.ValueSet, StringComparison.OrdinalIgnoreCase);
            return new Parameters.ParameterComponent()
            {
                Name = parameter.Item1,
                Value = !isResource ? CreateValue(type, parameter.Item1, parameter.Item2) : null,
                Resource = isResource ? CreateResource(parameter.Item1, parameter.Item2) : null,
            };
        }

        private Resource CreateResource(string name, string value)
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                return _parser.Parse<Resource>(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, Resources.ExpandInvalidParameterValue, name);
                throw new BadRequestException(
                    string.Format(Resources.ExpandInvalidParameterValue, name));
            }
        }

        private DataType CreateValue(Type type, string name, string value)
        {
            if (type == null || string.IsNullOrEmpty(value))
            {
                return null;
            }

            try
            {
                if (type == typeof(Canonical))
                {
                    return new Canonical(value);
                }

                if (type == typeof(Code))
                {
                    return new Code(value);
                }

                if (type == typeof(Integer))
                {
                    return new Integer(int.Parse(value));
                }

                if (type == typeof(FhirBoolean))
                {
                    return new FhirBoolean(bool.Parse(value));
                }

                if (type == typeof(FhirDateTime))
                {
                    return new FhirDateTime(value);
                }

                if (type == typeof(FhirUrl))
                {
                    return new FhirUrl(value);
                }

                if (type == typeof(FhirUri))
                {
                    return new FhirUri(value);
                }

                return new FhirString(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, Resources.ExpandInvalidParameterValue, name);
                throw new BadRequestException(
                    string.Format(Resources.ExpandInvalidParameterValue, name));
            }
        }

        private async Task<IReadOnlyList<Tuple<string, string>>> ProcessExpandParameters(IReadOnlyList<Tuple<string, string>> parameterList)
        {
            var parameters = parameterList?
                .GroupBy(x => x.Item1)
                .ToDictionary(x => x.Key, x => x.Select(y => y.Item2).ToList())
                ?? new Dictionary<string, List<string>>();
            if (!parameters.ContainsKey(TerminologyOperationParameterNames.Expand.Url)
                && !parameters.ContainsKey(TerminologyOperationParameterNames.Expand.ValueSet)
                && parameters.TryGetValue(TerminologyOperationParameterNames.Expand.Context, out var context))
            {
                if (Uri.TryCreate(context?.FirstOrDefault(), UriKind.Absolute, out var uri)
                    && !string.IsNullOrEmpty(uri?.Fragment))
                {
                    var url = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath}";
                    var path = uri.Fragment.TrimStart('#');
                    var definition = await _resourceResolver.FindStructureDefinitionAsync(url);
                    var valueSetUrl = definition?.Snapshot?.Element?
                        .Where(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase) && x.Binding?.ValueSet != null)
#if !Stu3
                        .Select(x => x.Binding.ValueSet)
#else
                        .Select(
                            x =>
                            {
                                return (x.Binding.ValueSet is FhirUri) ? ((FhirUri)x.Binding.ValueSet).Value : ((ResourceReference)x.Binding.ValueSet).Reference;
                            })
#endif
                        .FirstOrDefault();
                    if (string.IsNullOrEmpty(valueSetUrl))
                    {
                        throw new BadRequestException(
                            string.Format(Resources.ExpandInvalidParameterValue, TerminologyOperationParameterNames.Expand.Context));
                    }

                    var newParameterList = new List<Tuple<string, string>>(
                        parameterList.Where(x => !string.Equals(x.Item1, TerminologyOperationParameterNames.Expand.Context, StringComparison.OrdinalIgnoreCase)));
                    newParameterList.Add(
                        Tuple.Create(
                            TerminologyOperationParameterNames.Expand.Url,
                            valueSetUrl));
                    return newParameterList;
                }
                else
                {
                    throw new BadRequestException(
                        string.Format(Resources.ExpandInvalidParameterValue, TerminologyOperationParameterNames.Expand.Context));
                }
            }

            return parameterList;
        }

        private static ResourceElement CreateOperationOutcome(Exception exception)
        {
            if (exception == null)
            {
                return new OperationOutcome()
                {
                    Issue = new List<OperationOutcome.IssueComponent>
                    {
                        new OperationOutcome.IssueComponent()
                        {
                            Severity = OperationOutcome.IssueSeverity.Error,
                            Code = OperationOutcome.IssueType.Unknown,
                            Diagnostics = "An unknown error occurred during expand.",
                        },
                    },
                }.ToResourceElement();
            }

            if (exception is FhirOperationException)
            {
                var foex = (FhirOperationException)exception;
                if (foex?.Outcome != null)
                {
                    return foex.Outcome.ToResourceElement();
                }
            }

            if (exception is BadRequestException)
            {
                var brex = (BadRequestException)exception;
                if (brex?.Issues?.Any() ?? false)
                {
                    return new OperationOutcome()
                    {
                        Issue = new List<OperationOutcome.IssueComponent>(brex.Issues.Select(x => x.ToPoco())),
                    }.ToResourceElement();
                }
            }

            return new OperationOutcome()
            {
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent()
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = OperationOutcome.IssueType.Exception,
                        Diagnostics = exception.Message,
                    },
                },
            }.ToResourceElement();
        }

        private static ValueSet ProcessExpandedValueSet(ValueSet valueSet)
        {
            if (valueSet.Expansion?.Contains == null)
            {
                return valueSet;
            }
#if !Stu3
            foreach (var p in valueSet.Expansion.Contains.Where(x => x.Property?.Any() ?? false).Select(x => x.Property))
            {
                p.Clear();
            }
#endif
            return valueSet;
        }
    }
}
