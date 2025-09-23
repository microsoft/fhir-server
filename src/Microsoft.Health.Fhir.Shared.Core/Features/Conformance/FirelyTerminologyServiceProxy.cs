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
        private readonly ILogger<FirelyTerminologyServiceProxy> _logger;

        public FirelyTerminologyServiceProxy(
            ITerminologyService terminologyService,
            ILogger<FirelyTerminologyServiceProxy> logger)
        {
            EnsureArg.IsNotNull(terminologyService, nameof(terminologyService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _parser = new FhirJsonParser(
                new ParserSettings()
                {
                    PermissiveParsing = false,
                });
            _terminologyService = terminologyService;
            _logger = logger;
        }

        public async Task<ResourceElement> ExpandAsync(
            IReadOnlyList<Tuple<string, string>> parameters,
            string resourceId,
            CancellationToken cancellationToken)
        {
            try
            {
                var resource = await _terminologyService.Expand(
                    CreateExpandParameters(parameters),
                    resourceId);
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

            var isResource = string.Equals(parameter?.Item1, "valueSet", StringComparison.OrdinalIgnoreCase);
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
    }
}
