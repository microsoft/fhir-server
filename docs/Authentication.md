# Authentication
This article goes through the authentication settings for the FHIR server and how to make use of it in development and test scenarios.

## Authentication Settings
The current authentication settings exposed in configuration are the following:
```
"Security": {
    "Enabled":  true,
    "Authentication": {
        "Mode": "jwt",
        "Audience": "fhir-api",
        "Authority": "https://localhost:44348"
    }
```
|Element|Description|
|---|---|
|Enabled|Whether or not the server has any security enabled.|
|Authentication:Mode|The type of authentication enabled for the server. The only currently supported value is `jwt`.|
|Authentication:Audience|Identifies the recipient that the token is intended for. In this context it should be set to something representing the FHIR API itself.|
|Authentication:Authority|The issuer of the jwt token.|

## IdentityServer
For the F5 experience and test environments, IdentityServer is included and enabled to act as the authentication provider for the FHIR API. 

### IdentityServer Settings
```
"TestIdentityProvider": {
    "Audience": "fhir-api",
    "ClientId": "known-client-id",
    "ClientSecret": "known-client-secret" 
}
```

|Element|Description|
|---|---|
|Enabled|Whether or not IdentityServer should be enabled for the web server.|
|Audience|The audience that will be returned with a jwt token.|
|ClientId|The expected clientId that will be requesting the jwt token.|
|ClientSecret|The secret for the clientId for it to be authenticated.|

### Authenticating using built in IdentityServer
To obtain a token issue the following command.
```
POST /connect/token HTTP/1.1
Host: https://localhost:44348
Content-Type: application/x-www-form-urlencoded

client_id=known-client-id&client_secret=known-client-secret&grant_type=client_credentials&scope=fhir-api
```

To authenticate with the FHIR API take the `access_token` from the previous command and attach it as an `Authorization` header with the sytax: `Bearer {access_token}`.

Example token response
```
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
