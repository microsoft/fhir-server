// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Terminology;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Terminology
{
    public sealed class TerminologyOperator : ITerminologyOperator
    {
        private readonly ExternalTerminologyService _externalTerminoilogy = null;

        public TerminologyOperator(ExternalTerminologyService externalTSResolver)
        {
            try
            {
                EnsureArg.IsNotNull(externalTSResolver, nameof(externalTSResolver));
            }
            catch (ArgumentNullException)
            {
                throw new BadRequestException(Core.Resources.CouldNotAccessTerminologyEndpoint);
            }

            _externalTerminoilogy = externalTSResolver;
        }

        public Parameters TryLookUp(string system, string code)
        {
            Parameters param = new Parameters();
            param.Add("coding", new Coding(system, code));

            return TryLookUp(param);
        }

        public Parameters TryLookUp(Parameters param)
        {
            Task<Parameters> result = null;

            result = _externalTerminoilogy.Lookup(param, useGet: false);

            CheckResult(result, param, (param) => TryLookUp(param));

            return result.Result;
        }

        public Parameters TryValidateCode(Resource resource, string id, string code, string system, string display = null)
        {
            Parameters param = new Parameters();

            if (!string.IsNullOrWhiteSpace(display))
            {
                param.Add("coding", new Coding(system, code, display.Trim(' ')));
            }
            else
            {
                param.Add("coding", new Coding(system, code));
            }

            if (resource.TypeName == KnownResourceTypes.ValueSet)
            {
                param.Add("valueSet", (ValueSet)resource);
            }
            else
            {
                param.Add("codeSystem", (CodeSystem)resource);
            }

            return TryValidateCode(param);
        }

        public Parameters TryValidateCode(Parameters param)
        {
            Task<Parameters> result = null;

            foreach (var paramComponent in param.Parameter)
            {
                if (string.Equals(paramComponent.Name, KnownResourceTypes.ValueSet, StringComparison.OrdinalIgnoreCase))
                {
                    result = _externalTerminoilogy.ValueSetValidateCode(param, useGet: false);
                    break;
                }
                else if (string.Equals(paramComponent.Name, KnownResourceTypes.CodeSystem, StringComparison.OrdinalIgnoreCase))
                {
                    result = _externalTerminoilogy.CodeSystemValidateCode(param, useGet: false);
                    break;
                }
            }

            CheckResult(result, param, (param) => TryValidateCode(param));

            return result.Result;
        }

        public Resource TryExpand(Resource valueSet = null, FhirUri canonicalURL = null, int offset = 0, int count = 0)
        {
            Parameters param = new Parameters();

            AddExpandParams(param, valueSet, canonicalURL, offset, count);

            return TryExpand(param);
        }

        public Resource TryExpand(Parameters param)
        {
            Task<Resource> result = null;

            result = _externalTerminoilogy.Expand(param, useGet: false);

            CheckResult(result, param, (param) => TryExpand(param));

            return result.Result;
        }

        private static void AddExpandParams(Parameters param, Resource valueSet, FhirUri canonicalURL, int offset, int count)
        {
            if (valueSet != null)
            {
                param.Add("valueSet", (ValueSet)valueSet);
            }

            if (canonicalURL != null)
            {
                param.Add("url", canonicalURL);
            }

            if (offset != 0)
            {
                param.Add("offset", new Integer(offset));
            }

            if (count != 0)
            {
                param.Add("count", new Integer(count));
            }
        }

        private static void CheckResult<T>(Task<T> result, Parameters param, Action<Parameters> terminologyOperation)
        {
            try
            {
                result.Wait();
            }
            catch (AggregateException ex)
            {
                throw new BadRequestException(ex.InnerException.Message);
            }
        }
    }
}
