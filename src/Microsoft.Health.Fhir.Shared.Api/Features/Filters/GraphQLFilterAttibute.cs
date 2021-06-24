using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Api.Features.Filters
{
    public class GraphQLFilterAttribute : ActionFilterAttribute
    {
        private JsonSerializerSettings jsonSerializerSettings;

        public GraphQLFilterAttribute()
        {
            jsonSerializerSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
        }

        public override async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is FhirResult result && context.HttpContext.Request.Path.Value?.IndexOf("$graphql") > -1)
            {
                /*
                    var scheme = new Schema {}
                 */

                context.Result = new ObjectResult(new { prop = "Hello" });
            }

            await next();
        }
    }
}
