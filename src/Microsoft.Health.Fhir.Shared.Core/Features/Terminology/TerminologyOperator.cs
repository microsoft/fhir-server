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
                throw new BadRequestException("Cannot hit terminology service endpoint, please check that endpoint is correct / exists.");
            }

            _externalTerminoilogy = externalTSResolver;
        }

        public Parameters TryLookUp(string system, string code)
        {
            Parameters param = new Parameters();

            param.Add("coding", new Coding(system, code));

            return TryLookUp(param, false);
        }

        public Parameters TryLookUp(Parameters param, bool attempted)
        {
            Task<Parameters> result = null;

            result = _externalTerminoilogy.Lookup(param, useGet: false);

            CheckResult(result, param, attempted, (param, attempted) => TryLookUp(param, attempted));

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

            if (resource.TypeName == "ValueSet")
            {
                param.Add("valueSet", (ValueSet)resource);
            }
            else
            {
                param.Add("codeSystem", (CodeSystem)resource);
            }

            return TryValidateCode(param, false);
        }

        public Parameters TryValidateCode(Parameters param, bool attempted)
        {
            Task<Parameters> result = null;

            if (param.Parameter[1].Resource.TypeName == "ValueSet")
            {
                result = _externalTerminoilogy.ValueSetValidateCode(param, useGet: false);
            }
            else
            {
                result = _externalTerminoilogy.CodeSystemValidateCode(param, useGet: false);
            }

            CheckResult(result, param, attempted, (param, attempted) => TryValidateCode(param, attempted));

            return result.Result;
        }

        public Resource TryExpand(Resource valueSet = null, FhirUri canonicalURL = null, int offset = 0, int count = 0)
        {
            Parameters param = new Parameters();

            AddExpandParams(param, valueSet, canonicalURL, offset, count);

            return TryExpand(param, false);
        }

        public Resource TryExpand(Parameters param, bool attempted)
        {
            Task<Resource> result = null;

            result = _externalTerminoilogy.Expand(param, useGet: false);

            CheckResult(result, param, attempted, (param, attempted) => TryExpand(param, attempted));

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

        private static void CheckResult<T>(Task<T> result, Parameters param, bool attempted, Action<Parameters, bool> terminologyOperation)
        {
            try
            {
                result.Wait();
            }
            catch (AggregateException ex)
            {// If error is due to conformance statement compatibility issue, try again as that usually fixes the problem
                if (string.Equals(ex.InnerException.Message, "Cannot read the conformance statement of the server to verify FHIR version compatibility", StringComparison.OrdinalIgnoreCase))
                {
                    if (!attempted)
                    {
                        terminologyOperation(param, true);
                    }
                }

                throw new BadRequestException(ex.InnerException.Message);
            }
        }
    }
}
