// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Validation.Narratives
{
    public abstract class NarrativeDataTestBase
    {
        /// <summary>
        /// Malicious Url Data Source
        /// </summary>
        /// <remarks>
        /// These examples from https://www.owasp.org/index.php/OWASP_Testing_Guide_Appendix_C:_Fuzz_Vectors
        /// </remarks>
        public static IEnumerable<object[]> XssStrings()
        {
            return new object[]
            {
                "<div>'';!--\"<XSS>=&{()}</div>",
                "<div><?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><foo><![CDATA[<]]>SCRIPT<![CDATA[>]]>alert('gotcha');<![CDATA[<]]>/SCRIPT<![CDATA[>]]></foo></div>",
                "<div><a onclick=\"alert('gotcha');\"></a></div>",
                "<div><div id=\"nested\"><span><a onclick=\"alert('gotcha');\"></a></span></div></div>",
                "<div><IMG SRC=\"jav &#x0D;ascript:alert(<WBR>'XSS');\"></div>",
                "<div><img%20src%3D%26%23x6a;%26%23x61;%26%23x76;%26%23x61;%26%23x73;%26%23x63;%26%23x72;%26%23x69;%26%23x70;%26%23x74;%26%23x3a;alert(%26quot;%26%23x20;XSS%26%23x20;Test%26%23x20;Successful%26quot;)></div>",
                "<div><IMGSRC=&#106;&#97;&#118;&#97;&<WBR>#115;&#99;&#114;&#105;&#112;&<WBR>#116;&#58;&#97;&#108;&#101;&<WBR>#114;&#116;&#40;&#39;&#88;&#83<WBR>;&#83;&#39;&#41></div>",
                "<div><script src=http://www.example.com/malicious-code.js></script></div>",
                "<div>%22%27><img%20src%3d%22javascript:alert(%27%20XSS%27)%22></div>",
                "<div>\"'><img%20src%3D%26%23x6a;%26%23x61;%26%23x76;%26%23x61;%26%23x73;%26%23x63;%26%23x72;%26%23x69;%26%23x70;%26%23x74;%26%23x3a;</div>",
                "<div>\"><script>alert(\"XSS\")</script>&</div>",
                "<div>\"><STYLE>@import\"javascript:alert('XSS')\";</ STYLE ></div>",
                "<div>http://www.example.com/>\"><script>alert(\"XSS\")</script>&</div>",
            }.Select(x => new[] { x });
        }
    }
}
