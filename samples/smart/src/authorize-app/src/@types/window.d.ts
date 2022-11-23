export { };

declare global {
    interface Window {
        ENV_CONFIG: {
            REACT_APP_AAD_APP_CLIENT_ID: string;
            REACT_APP_AAD_APP_TENANT_ID: string;
            REACT_APP_AAD_APP_REDIRECT_URI: string;
            REACT_APP_API_BASE_URL: string;
            REACT_APP_APPLICATIONINSIGHTS_CONNECTION_STRING: string;
        }
    }
}