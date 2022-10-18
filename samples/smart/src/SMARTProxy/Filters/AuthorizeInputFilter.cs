using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using SMARTProxy.Configuration;
using SMARTProxy.Extensions;
using SMARTProxy.Models;
using System.Net;
using System.Text.Json;

namespace SMARTProxy.Filters
{
    public class AuthorizeInputFilter : IInputFilter
    {
        private readonly ILogger _logger;
        private readonly SMARTProxyConfig _configuration;

        public AuthorizeInputFilter(ILogger<AuthorizeInputFilter> logger, SMARTProxyConfig configuration)
        {
            _logger = logger;
            _configuration = configuration;
            id = Guid.NewGuid().ToString();
        }

        public event EventHandler<FilterErrorEventArgs> OnFilterError;

        public string Name => nameof(AuthorizeInputFilter);

        public StatusType ExecutionStatusType => StatusType.Normal;

        private readonly string id;
        string IFilter.Id => id;

        public async Task<OperationContext> ExecuteAsync(OperationContext context)
        {
            // Only execute for authorize request
            if (!context.Request!.RequestUri!.LocalPath.Contains("authorize"))
            {
                return context;
            }

            _logger?.LogInformation("Entered {Name}", Name);

            // Get and parse SMART launch params from the request
            var launchContext = await ParseLaunchContext(context.Request);

            // Verify required SMART launch params are not null
            if (!launchContext.ValidateLaunchContext())
            {
                context.IsFatal = true;
                context.ContentString = "Missing required SMART Launch parameters";
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogInformation("Bad request - required launch parameters missing. {launchContext}", JsonSerializer.Serialize(launchContext));
                return context;
            }

            // Verify response_type is "code"
            if (!launchContext.ValidateResponseType())
            {
                context.IsFatal = true;
                context.ContentString = "Invalid response_type. Only 'code' is supported";
                context.StatusCode = HttpStatusCode.BadRequest;
                _logger.LogInformation("Invalid response type  {responseType}.", launchContext.ResponseType);
                return context;
            }

            // Parse scopes
            var scopes = launchContext.Scope?.ParseScope("06f9d5a9-d4c3-4b2e-8992-53bd56c47d52");

            // TEST - REMOVE
            launchContext.Aud = "api://06f9d5a9-d4c3-4b2e-8992-53bd56c47d52";

            // Build the aad authorize url
            var authUrl = "https://login.microsoftonline.com";
            var authPath = $"{_configuration.TenantId}/oauth2/v2.0/authorize";
            var redirectUrl = $"{authUrl}/{authPath}";
            var redirect_querystring = launchContext.ToRedirectQueryString(scopes!);
            var newRedirectUrl = $"{redirectUrl}?{redirect_querystring}";

            context.StatusCode = HttpStatusCode.Redirect;
            context.Headers.Add(new HeaderNameValuePair("Location", newRedirectUrl, CustomHeaderType.ResponseStatic));
            context.Headers.Add(new HeaderNameValuePair("Origin", "http://localhost", CustomHeaderType.ResponseStatic));
            context.Request.RequestUri = new Uri(newRedirectUrl);

            return context;
        }

        static async Task<LaunchContext> ParseLaunchContext(HttpRequestMessage req)
        {
            LaunchContext launchContext;

            if (req.Method == HttpMethod.Post)
            {
                if (req.Content!.Headers.GetValues("Content-Type").Single().Contains("application/x-www-form-urlencoded"))
                {
                    if (req.Content is null)
                        throw new Exception("Body must contain data");

                    launchContext = new LaunchContextBuilder()
                        .FromNameValueCollection(await req.Content.ReadAsFormDataAsync())
                        .Build();
                }
                else
                {
                    throw new Exception("Unsupported Content-Type");
                }
            }
            else if (req.Method == HttpMethod.Get)
            {
                launchContext = new LaunchContextBuilder()
                    .FromNameValueCollection(req.RequestUri.ParseQueryString())
                    .Build();
            }
            else
            {
                throw new Exception("Unsupported HTTP method");
            }

            return launchContext;
        }
    }
}
