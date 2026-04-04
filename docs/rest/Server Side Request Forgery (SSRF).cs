Server Side Request Forgery (SSRF)
Server-Side Request Forgery (also known as SSRF) is a web security vulnerability in which an attacker can manipulate the server-side application to make network requests to an arbitrary endpoint. This can lead to critical vulnerabilities and high-impact exploits. You can read more about SSRF at https://aka.ms/ssrf/sdl

CodeQL has flagged your service because it detected user-tainted input (e.g. untrusted input from a user or service) being able to control the destination of an outgoing web request, by manipulating the hostname of a URI. The query will continue to alert if the AntiSSRF library is not implemented correctly.

If upon review, you determine that the input identified by CodeQL flows to the URL path/query, not the URL host, see the note at the end of the guidance for using the UriBuilder to remediate.

If upon review, you determine that the input identified by CodeQL is not user-tainted and the resulting URL does not incorporate any user input (e.g. untrusted input from a user or service), see https://aka.ms/codeql#guidance-on-suppressions

To view the full code flow for your alert see CodeQL Portal: View Code Snippet and Code Flow

This query checks for SSRF risk, using known .NET APIs that may be manipulated to send requests to an arbitrary endpoint.

Recommendation
CodeQL has determined that a URI in your service's code contains user-tainted input in the hostname. This URI must be validated as described below:

Validating Known URIs in .NET
Regardless of the .NET framework used, if your service is expecting that the URI identified must always belong to a specific domain (i.e. "azure.com"), use the AntiSSRF InDomain Method to ensure that the URI belongs to one of the specified domains.

This example shows incorrect implementation. There is no data flow path between the variable used in the InDomain method and in the web request.

string customer_input = "https://useraccount.contoso.com";
string domain = "contoso.com";
var customer_input_uri = new Uri(customer_input);

if (URIValidate.InDomain(customer_input_uri, domain))
{
    // BAD: the validated customer_input_uri variable is not used in the web request
    (new HttpClient()).GetAsync(customer_input);
}
else
{ ... }
These examples show correct implementation. Note that the same variable is used in the InDomain method and in the web request. There must be a data flow path between these, or it will not be considered remediated.

using System;
using System.Net;
using Microsoft.Internal.AntiSSRF;

// Example 1: InDomain will return true
string customer_input = "https://useraccount.contoso.com";
string domain = "contoso.com";
if (URIValidate.InDomain(customer_input, domain))
{
    // The customer_input belongs to the Contoso domain
    // GOOD: the validated customer_input variable is used in the web request
    client.GetAsync(customer_input); 
}
else
{
    // The customer_input does not belong to the Contoso domain. Do not send a web request to customer_input.
}


// Example 2: InDomain will return true
string customer_input = "https://user@contoso.com";
string domain = "contoso.com";

var customer_input_uri = new Uri(customer_input);

if (URIValidate.InDomain(customer_input_uri, domain))
{
    // The customer_input_uri belongs to the Contoso domain.
    // GOOD: the validated customer_input_uri variable is used in the web request
    client.GetAsync(customer_input_uri); 
}
else
{
    // The customer_input_uri does not belong to the Contoso domain. Do not send a web request to customer_input.
}


// Example 3: InDomain will return false
string customer_input = "https://google.com";
string[] domains = new string[] { "azure.com", "edge.com", "contoso.com" };
if (URIValidate.InDomain(customer_input, domains))
{
    // The customer_input belongs to the domain list
    client.GetAsync(new Uri(customer_input)); // GOOD: the validated customer_input variable is used in the web request
}
else
{
    // The customer_input does not belong to the domain list. Do not send a web request to customer_input.
}
Validating Unknown URIs in .NET
If the URI detected by CodeQL can belong to any domain, you must ensure that it does not resolve to sensitive internal IP addresses.

For .NET Core applications, use version 1.2.3 of the AntiSSRF library. A SocketsHttpHandler will be returned from policy.GetHandler().
For .NET Framework applications, use version 2.0.0 of the AntiSSRF library. An HttpClientHandler will be returned from policy.GetHandler().
See AntiSSRF Quickstart for installation instructions.
The AntiSSRF Library must be used here in the following manner:

1. Create an AntiSSRF Policy.

If you use the defaults of this policy, the default ruleset will be applied.

2. (Optional) Customize defaults as needed

For example, if you'd like to use the default policy, but also allow a certain IP range, you can use AddAllowedAddresses to add that range to the list of allowed destination IPs.

You can also start with an empty policy (Set useDefaults:false) and use the aforementioned method AddAllowedAddresses to edit your own policy.

Listed below are all of the methods you can use to further customize your policy:

AddAllowedAddresses: This method will alter the existing AntiSSRFPolicy instance by adding a list of IPv4/IPv6 addresses or subnets that the caller will be allowed access to.
AddDeniedAddresses: This method will alter the existing AntiSSRFPolicy instance by adding a list of IPv4/IPv6 addresses or subnets that the caller will be denied access to.
AddDeniedHeaders: This method will alter the existing AntiSSRFPolicy instance to ensure that web requests with the specified headers will not be sent.
AddRequiredHeaders: This method will alter the existing AntiSSRFPolicy instance to ensure that web requests must contain the specified headers.
SetAllowPlainTextHttp: This method will alter the AntiSSRFPolicy to allow HTTP (rather than only HTTPS) requests.
3. Use GetHandler to extract the handler corresponding to the specified AntiSSRFPolicy.

4. Pass the resulting handler as a parameter in your new HTTP client.

Example using GetHandler
var policy = new AntiSSRFPolicy();
policy.SetDefaults();

//Examples
var handler = policy.GetHandler();
var HttpClient client = new HttpClient(handler, false);

var responseString = await client.GetStringAsync("https://contoso.com"); // This is allowed.

responseString = await client.GetStringAsync("http://localhost.com"); // This will throw an exception.
NOTE: The remediation pattern for setting the handler in IServiceCollection/IHttpClientFactory is not supported by the query yet. Review the implementation carefully, then see https://aka.ms/codeql#guidance-on-suppressions

The .NET Core version of the AntiSSRF library returns a SocketsHttpHandler from policy.GetHandler(). See Using IHttpClientFactory together with SocketsHttpHandler.
The .NET Framework version of the AntiSSRF library returns an HttpClientHandler from policy.GetHandler(). See Configure the HttpMessageHandler.
Ensure other settings are correct, such as named vs anonymous clients and typed vs non-typed clients in the IServiceCollection and where the clients are created.
Review the HttpClient lifetime management settings.
Other Cases: If InDomain or GetHandler cannot be used
IMPORTANT: Use the following method (isNonroutableNetworkAddress) only if the recommended methods above cannot be used.

If this method is used, all additional directions must be implemented, otherwise your service will NOT be fully protected against SSRF attacks.

Note: IsNonroutableNetworkAddress(Uri, Policy) and IsNonroutableNetworkAddress(string, Policy) overloaded methods are no longer supported by the CodeQL query. Instead, use IPAddress.TryParse method on the Uri.host or string, then follow all additional directions.

If you are unable to use the InDomain method or handlers recommended above, you can also use the IsNonroutableNetworkAddress method. This method does NOT provide the same protections as the recommendations listed already. Thus, the directions on the method page must be followed to provide similar protection. These have also been noted below:

Additional Steps to take when using IsNonroutableNetworkAddress
1. Ensure that DNS resolution is done only once.

DNS servers provide a TTL (time-to-live) value, which indicates how many seconds a DNS resolver will cache a query before requesting a new one. Low TTL values can indicate a DNS Rebinding or DNS TOCTOU (time of check, time of use) vulnerability. This can be exploited in the following way:

Contoso gets a URL evil.net which resolves to 20.112.80.43
Security check flags this as a (safe) external address.
DNS cache expires, evil.net now resolves to 127.0.0.1.
Service connects to unsafe localhost address.
The following code is susceptible to SSRF, as HttpClient internally performs a second DNS resolution upon sending the web request.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Net;
using Microsoft.Internal.AntiSSRF;

// THE FOLLOWING CODE IS VULNERABLE TO SSRF

public class HomeController : Controller
{
    public Index(string host = "https://contoso.com")
    {
        Uri customer_input_uri = new Uri(host);

        var policy = new AntiSSRFPolicy(useDefaults:true);

        IPAddress[] resolvedIPs = await Dns.GetHostAddressesAsync(customer_input_uri.Host);

        if (URIValidate.IsNonroutableNetworkAddress(resolvedIPs[0], policy))
        {
          // The customer_input_uri resolves to a nonroutable IP address. Do not send a web request to customer_input_uri.
        }
        else
        {
            // The customer_input_uri does not resolve to a nonroutable IP address.

            // BAD: The attacker could change the DNS entry of https://contoso.com to point to localhost at this point in time.

            using HttpClient client = new HttpClient();
            var response = await client.SendAsync(customer_input_uri);

            // HttpClient internally performs another DNS lookup. This time, https://contoso.com resolves to 127.0.0.1. Thus, the web request is sent to 127.0.0.1, and our protections are bypassed.
        }
    }
}
To avoid this bypass, you must conduct a DNS lookup only once.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Net;
using Microsoft.Internal.AntiSSRF;

// THE FOLLOWING CODE FIXES THE PREVIOUSLY MENTIONED BYPASS

public class HomeController : Controller
{
    public Index(string host = "https://contoso.com")
    {
        Uri customer_input_uri = new Uri(host);

        var policy = new AntiSSRFPolicy(useDefaults:true);

        IPAddress[] resolvedIPs = await Dns.GetHostAddressesAsync(customer_input_uri.Host);
        
        if (URIValidate.IsNonroutableNetworkAddress(resolvedIPs[0], policy))
        {
            // The customer_input_uri resolves to a nonroutable IP address. Do not send a web request to customer_input_uri.
        }
        else
        {
            // The customer_input_uri does not resolve to a nonroutable IP address.

            // The attacker could change the DNS entry of https://contoso.com to point to localhost at this point in time, but this no longer matters, since we are not conducting another DNS lookup.

            var request = new HttpRequestMessage(); // Note: a URL is not set on the constructor

            request.Headers.Host = customer_input_uri.Host;

            if (resolvedIPs[0].AddressFamily == AddressFamily.InterNetwork)
            {
                request.RequestUri = new Uri($"{_scheme}://{resolvedIPs[0].ToString()}:{_port}{_pathAndQuery}");
            }
            else if (resolvedIPs[0].AddressFamily == AddressFamily.InterNetworkV6)
            {
                request.RequestUri = new Uri($"{_scheme}://[{resolvedIPs[0].ToString()}]:{_port}{_pathAndQuery}");
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // GOOD: With these changes, HttpClient will connect directly to resolvedIPs[0].
        }
    }
}
2. HTTP redirects should be disabled when possible. Otherwise, the redirects should be validated to ensure that web requests are not made to any of the abovementioned IP addresses.

Redirects can be exploited in the following way:

Contoso gets a URL evil.net which resolves to 20.112.80.43
Security check flags this as a (safe) external address.
Contoso sends a web request to evil.net.
evil.net responds with a 302 redirection to http://127.0.0.1
Service connects to unsafe localhost address.
Redirects include HTTP responses 301, 302, 303, 307, 401, etc.

Notes
In older versions of the AntiSSRF library, the URIValidate class has namespace Microsoft.Internal.URIValidator.URIValidate. This is deprecated and the new namespace is Microsoft.Internal.AntiSSRF.URIValidate.
If you are using the AntiSSRF library and still have an alert, upgrade the library to the latest version.
String concatenation should not be used to dynamically generate URLs. Instead, use the UriBuilder Class to specify the URL host, path, and query. This is similar to parameterizing a SQL query.
If you are unsure whether you have an SSRF in your code and need more help, please email antissrf@microsoft.com or attend office hours https://aka.ms/antissrf/support

Other Case: alert is for URL path/query, not URL host
This CodeQL query targets untrusted input flowing to the host of a request URL. For example, env in $"https://mystorage.blob{env}.azure.net/"

However, there are cases where CodeQL cannot statically determine that the untrusted input flows to the URL path/query vs the URL host, and there will be an alert in these cases.

If the alert is for a request URL path or query, switch to UriBuilder Class.
Using the UriBuilder Class will not sanitize the path or query to prevent issues like URL path traversal, open redirect, etc. However, it will remediate alerts for SSRF.
Using the UriBuilder Class will not remediate alerts where the sink is the host, only the path or query.
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class HomeController : Controller
{
    public async Task<IActionResult> Index(string host, string region, string env, string blobContainerName, string blobName)
    {
        // BAD: string concatenation is used to dynamically generate the URL
        var url = $"https://{host}/{region}?env={env}";
        
        // BETTER: UriBuilder is used to dynamically generate the URL
        // NOTE: This does not sanititze Uri.PathAndQuery argument, however `region` and `env` will no longer be flagged for SSRF
        Uri client_uri = new UriBuilder(Uri.UriSchemeHttps, host, 443, region, $"?env={env}").Uri;
        
        // BETTER: UriBuilder is used to dynamically generate the URL
        // NOTE: This does not sanititze Uri.Path or Uri.Query properties, however `region` and `env` will no longer be flagged for SSRF
        var uriBuilder = new UriBuilder
        {
            Scheme = Uri.UriSchemeHttps,
            Host = host,
            Path = region,
            Query = $"env={env}",
        };
        
        // For a relative Uri, only set the `Path` and `Query`
        // The UriBuilder will set defaults of `http` and `localhost` for the `Scheme` and `Host`, which will not be used
        var uriBuilder2 = new UriBuilder
        {
            Path = region,
            Query = $"env={env}",
        };
        
        // Create a Relative Uri with only the relevant section from the builder (`Path`, `Query`, or `PathAndQuery`)
        var url = new Uri(uriBuilder2.Uri.PathAndQuery, UriKind.Relative);
        
    }
}
Helper Methods and AntiSSRF Library
The following code patterns are supported for using the URIValidate methods in a common helper method or class.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Threading.Tasks;

public class HomeController : Controller
{
    // Example 1
    public void Index(Uri uri)
    {
        // GOOD: helper method is used in the if-statement
        if (AntiSsrfHelperMethodWithReturn(uri))
        {
            throw new Exception("AntiSSRF validation failed");
        }

        var client = new BlobContainerClient(uri, new AzureCliCredential());
    }
  
    // GOOD: supports Uri, string, IPAddress parameter types
    private bool AntiSsrfHelperMethodWithReturn(Uri uri)
    {
        // GOOD: the parameter from AntiSsrfHelperMethodWithReturn flows to the URIValidate method
        bool isInvalidStorageUri = !URIValidate.InAzureStorageDomain(uri) 
                                    && !URIValidate.InDomain(uri, [".contoso.com"]);

        // GOOD: URIValidate method call flows to boolean return statement
        return isInvalidStorageUri;
    }
    
    // Example 2
    public void Index2(Uri uri)
    {
        AntiSsrfHelperMethod(uri);
        client.GetAsync(uri);
    }
    
    // GOOD: supports Uri, string, IPAddress parameter types
    private void AntiSsrfHelperMethod(Uri uri)
    {
        // GOOD: the parameter from AntiSsrfHelperMethod flows to the URIValidate method
        if (!URIValidate.InDomain(uri, "contoso.com"))
        {
            throw new Exception("AntiSSRF validation failed");
        }
        
        // NOTE: the if-statement with the URIValidate method must be the last code block in the helper method
        // If there is any code after the if-statement, AntiSsrfHelperMethod will not be considered a sanitizer
    }
}
References
SDL: Microsoft.Security.SystemsADM.10107
strike: Server-Side Request Forgery (SSRF)
OWASP: Server Side Request Forgery
Common Weakness Enumeration: CWE-918.