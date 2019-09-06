// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public class AuditHeaderReader : IAuditHeaderReader
    {
        private readonly AuditConfiguration _auditConfiguration;

        public AuditHeaderReader(IOptions<AuditConfiguration> auditConfiguration)
        {
            _auditConfiguration = auditConfiguration.Value;
        }

        public IReadOnlyDictionary<string, string> Read(HttpContext httpContext)
        {
            if (!httpContext.Items.ContainsKey(AuditConstants.CustomAuditHeaderKeyValue))
            {
                var customHeaders = new Dictionary<string, string>();

                foreach (var headerName in httpContext.Request.Headers.Keys)
                {
                    if (headerName.StartsWith(_auditConfiguration.CustomAuditHeaderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var headerValue = httpContext.Request.Headers[headerName].ToString();
                        if (headerValue.Length > AuditConstants.MaximumLengthOfCustomHeader)
                        {
                            throw new AuditHeaderException(headerName, headerValue.Length);
                        }

                        customHeaders[headerName] = headerValue;
                    }
                }

                if (customHeaders.Count > AuditConstants.MaximumNumberOfCustomHeaders)
                {
                    throw new AuditHeaderException(customHeaders.Count);
                }

                httpContext.Items[AuditConstants.CustomAuditHeaderKeyValue] = customHeaders;
                return customHeaders;
            }
            else
            {
                return httpContext.Items[AuditConstants.CustomAuditHeaderKeyValue] as IReadOnlyDictionary<string, string>;
            }
        }
    }
}
