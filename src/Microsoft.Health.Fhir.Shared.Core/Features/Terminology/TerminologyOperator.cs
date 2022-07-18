// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
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
            _externalTerminoilogy = externalTSResolver;
        }

        public Parameters TryLookUp(string system, string code)
        {
            Parameters param = new Parameters();

            param.Add("coding", new Coding(code, system));

            Task<Parameters> result = null;

            try
            {
                result = _externalTerminoilogy.Lookup(param, useGet: false);
            }
            catch (NullReferenceException)
            {
                throw new BadRequestException("Cannot hit terminology service endpoint, please check that endpoint is correct / exists.");
            }

            try
            {
                result.Wait();
            }
            catch (Exception ex)
            {
                throw new BadRequestException(ex.InnerException.Message);
            }

            return result.Result;
        }

        public Parameters TryLookUp(Parameters param)
        {
            Task<Parameters> result = null;

            try
            {
                result = _externalTerminoilogy.Lookup(param, useGet: false);
            }
            catch (NullReferenceException)
            {
                throw new BadRequestException("Cannot hit terminology service endpoint, please check that endpoint is correct / exists.");
            }

            try
            {
                result.Wait();
            }
            catch (Exception ex)
            {
                throw new BadRequestException(ex.InnerException.Message);
            }

            return result.Result;
        }

        public Parameters TryValidateCode(Resource resource, string id, string code, string system = null, string display = null)
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

            return TryValidateCode(param);
        }

        public Parameters TryValidateCode(Parameters param)
        {
            Task<Parameters> result = null;

            try
            {
                if (param.Parameter[1].Resource.TypeName == "ValueSet")
                {
                    result = _externalTerminoilogy.ValueSetValidateCode(param, useGet: false);
                }
                else
                {
                    result = _externalTerminoilogy.CodeSystemValidateCode(param, useGet: false);
                }
            }
            catch (NullReferenceException)
            {
                throw new BadRequestException("Cannot hit terminology service endpoint, please check that endpoint is correct / exists.");
            }

            try
            {
                result.Wait();
            }
            catch (Exception ex)
            {
                throw new BadRequestException(ex.InnerException.Message);
            }

            return result.Result;
        }
    }
}
