# Authentication
This article goes through the authentication settings for the FHIR server and how to make use of it in development and test scenarios.

## Authentication Settings
The current authentication settings exposed in configuration are the following:
```json
"FhirServer" : {
    "Security": {
        "Enabled":  true,
        "Authentication": {
            "Audience": "fhir-api",
            "Authority": "https://localhost:44348"
        }
    }
}
```
|Element|Description|
|---|---|
|Enabled|Whether or not the server has any security enabled.|
|Authentication:Audience|Identifies the recipient that the token is intended for. In this context it should be set to something representing the FHIR API itself.|
|Authentication:Authority|The issuer of the jwt token.|

## Development Identity Provider
For the F5 experience and test environments, an in-process identity provider is included that can act as the authentication provider for the FHIR API. 

### Enabling DevelopmentIdentityProvider
To enable the development identity provider, add the following code to your app configuration:

```csharp
builder.AddDevelopmentAuthEnvironment("path/to/testAuthEnvironment.json");
```

The code above, along with the `TestAuthEnvironment.json` file will configure and start the identity provider for use by the FHIR API.

### TestAuthEnvironment.json
The `testauthenvironment.json` file located in the root directory holds the configuration used for the server. **This file is meant only for local and test environments.** The items represented in this file include the roles available for the API as well as users and client applications that have access to the API. During the F5 experience and local testing, the password/secret for both users and client applications is the same as the id of the item. 

### Authenticating using built in IdentityServer
To obtain a token issue the following command.
```
POST /connect/token HTTP/1.1
Host: https://localhost:44348
Content-Type: application/x-www-form-urlencoded

client_id=serviceclient&client_secret=serviceclient&grant_type=client_credentials&scope=fhir-api
```

To authenticate with the FHIR API take the `access_token` from the previous command and attach it as an `Authorization` header with the sytax: `Bearer {access_token}`.

Example token response
```json
{
    "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6Ijc4YWJlMDM0OGEyNDg4NzU0MmUwOGJjNTg3YWFjY2Q4IiwidHlwIjoiSldUIn0.eyJuYmYiOjE1MjM1NTQ3OTQsImV4cCI6MTUyMzU1ODM5NCwiaXNzIjoiaHR0cDovL2xvY2FsaG9zdDo1MzcyNyIsImF1ZCI6WyJodHRwOi8vbG9jYWxob3N0OjUzNzI3L3Jlc291cmNlcyIsImZoaXItYXBpIl0sImNsaWVudF9pZCI6Imtub3duLWNsaWVudC1pZCIsInNjb3BlIjpbImZoaXItYXBpIl19.pZWIWy3RdDHp5zgcYs8bb9VrxIHXbYu8LolC3YTy6xWsPxMoPUQwbAltYmC6WDXFiDygpsC5ofkGlR4BH0Bt1FMvFWqFYhPcOOKvBqLLc055EHZfTcNcmiUUf4y4KRuQFqWZsH_HrfWwykSGVio2OnYcQvytrbjAi_EzHf2vrHJUHX2JFY4A_F6WpJbQiI1hUVEOd7h1jfmAptWlNGwNRbCF2Wd1Hf_Hodym8mEOKQz21VHdvNJ_B-owPMvLjalV5Nrvpv0yC9Ly5YablrkzB583eHwQNSA7A4ZMm49O8MWv8kUwwF5TF0lJJDyyw3ruqmPWCM-058chenU0rtCsPQ",
    "expires_in": 3600,
    "token_type": "Bearer"
}
```

Example Authorization header
```
Authorization: Bearer eyJhbGciOiJSUzI1NiIsImtpZCI6Ijc4YWJlMDM0OGEyNDg4NzU0MmUwOGJjNTg3YWFjY2Q4IiwidHlwIjoiSldUIn0.eyJuYmYiOjE1MjM1NTQ3OTQsImV4cCI6MTUyMzU1ODM5NCwiaXNzIjoiaHR0cDovL2xvY2FsaG9zdDo1MzcyNyIsImF1ZCI6WyJodHRwOi8vbG9jYWxob3N0OjUzNzI3L3Jlc291cmNlcyIsImZoaXItYXBpIl0sImNsaWVudF9pZCI6Imtub3duLWNsaWVudC1pZCIsInNjb3BlIjpbImZoaXItYXBpIl19.pZWIWy3RdDHp5zgcYs8bb9VrxIHXbYu8LolC3YTy6xWsPxMoPUQwbAltYmC6WDXFiDygpsC5ofkGlR4BH0Bt1FMvFWqFYhPcOOKvBqLLc055EHZfTcNcmiUUf4y4KRuQFqWZsH_HrfWwykSGVio2OnYcQvytrbjAi_EzHf2vrHJUHX2JFY4A_F6WpJbQiI1hUVEOd7h1jfmAptWlNGwNRbCF2Wd1Hf_Hodym8mEOKQz21VHdvNJ_B-owPMvLjalV5Nrvpv0yC9Ly5YablrkzB583eHwQNSA7A4ZMm49O8MWv8kUwwF5TF0lJJDyyw3ruqmPWCM-058chenU0rtCsPQ
```
