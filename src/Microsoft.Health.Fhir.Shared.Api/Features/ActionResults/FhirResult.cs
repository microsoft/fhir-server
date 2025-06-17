// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    /// <summary>
    /// Handles the output of a FHIR MVC Action Method
    /// </summary>
    public class FhirResult : ResourceActionResult<IResourceElement>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirResult" /> class.
        /// </summary>
        public FhirResult()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirResult" /> class.
        /// </summary>
        /// <param name="resource">The resource.</param>
        public FhirResult(IResourceElement resource)
            : base(resource)
        {
        }

        /// <summary>
        /// Creates a FHIR result with the specified parameters
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="setETagheader">The value indicating whether to add the ETag header.</param>
        /// <param name="setLastModifiedHeader">The value indicating whether to add the LastModified header.</param>
        /// <param name="setLocationHeader">The value indicating whether to add the Location header.</param>
        /// <param name="urlResolver">The url resolver.</param>
        /// <param name="returnPreference">The return preference.</param>
        /// <param name="operationOutcomeMessage">The operation outcome message.</param>
        public static FhirResult Create(
            IResourceElement resource,
            HttpStatusCode statusCode = HttpStatusCode.OK,
            bool setETagheader = false,
            bool setLastModifiedHeader = false,
            bool setLocationHeader = false,
            IUrlResolver urlResolver = null,
            ReturnPreference? returnPreference = null,
            string operationOutcomeMessage = null)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            var result = new FhirResult(resource)
            {
                StatusCode = statusCode,
            };

            if (setETagheader)
            {
                result.SetETagHeader();
            }

            if (setLastModifiedHeader)
            {
                result.SetLastModifiedHeader();
            }

            if (setLocationHeader && urlResolver != null)
            {
                result.SetLocationHeader(urlResolver);
            }

            if (returnPreference != null)
            {
                if (returnPreference == ReturnPreference.Minimal)
                {
                    var minimalResult = new FhirResult(null)
                    {
                        StatusCode = statusCode,
                    };

                    foreach (var header in result.Headers)
                    {
                        minimalResult.Headers[header.Key] = header.Value;
                    }

                    return minimalResult;
                }
                else if (returnPreference == ReturnPreference.OperationOutcome)
                {
                    var operationOutcome = new OperationOutcome();
                    operationOutcome.Issue.Add(
                        new OperationOutcome.IssueComponent()
                        {
                            Severity = OperationOutcome.IssueSeverity.Information,
                            Diagnostics = operationOutcomeMessage,
                            Details = new CodeableConcept()
                            {
                                Text = operationOutcomeMessage,
                            },
                        });

                    var operationOutcomeResult = new FhirResult(operationOutcome.ToResourceElement())
                    {
                        StatusCode = statusCode,
                    };

                    foreach (var header in result.Headers)
                    {
                        operationOutcomeResult.Headers[header.Key] = header.Value;
                    }

                    return operationOutcomeResult;
                }
            }

            return result;
        }

        /// <summary>
        /// Creates a Gone response
        /// </summary>
        public static FhirResult Gone()
        {
            return new FhirResult
            {
                StatusCode = HttpStatusCode.Gone,
            };
        }

        /// <summary>
        /// Returns a NotFound response
        /// </summary>
        public static FhirResult NotFound()
        {
            return new FhirResult
            {
                StatusCode = HttpStatusCode.NotFound,
            };
        }

        /// <summary>
        /// Returns a NoContent response
        /// </summary>
        public static FhirResult NoContent()
        {
            return new FhirResult
            {
                StatusCode = HttpStatusCode.NoContent,
            };
        }

        protected override object GetResultToSerialize()
        {
            if (Result is ResourceElement)
            {
                return (Result as ResourceElement)?.ToPoco();
            }
            else if (Result is RawResourceElement)
            {
                return Result;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public override string GetResultTypeName()
        {
            return Result?.InstanceType;
        }
    }
}
