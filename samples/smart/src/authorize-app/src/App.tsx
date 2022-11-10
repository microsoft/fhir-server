import { MsalProvider, MsalAuthenticationTemplate } from "@azure/msal-react";
import { IPublicClientApplication, InteractionType } from "@azure/msal-browser";
import {  ThemeProvider } from '@fluentui/react';

import ProvideAppContext from './AppContext';
import Home from "./pages/Home";
import { scopes } from './Config';
import AuthLoading from "./components/authLoading"
import AuthError from "./components/authError"
import { Stack } from "@fluentui/react";
import ErrorMessage from "./ErrorMessage";

type AppProps = {
  pca: IPublicClientApplication
};

function App({ pca }: AppProps) {

  const authRequest = {
    scopes: scopes
  };


  return (
    <MsalProvider instance={pca}>
      <MsalAuthenticationTemplate
        interactionType={InteractionType.Redirect}
        authenticationRequest={authRequest}
        errorComponent={AuthError}
        loadingComponent={AuthLoading}
      >
        <ThemeProvider>
        <ProvideAppContext>
          <Stack>
            <ErrorMessage />
            <Home />
          </Stack>
        </ProvideAppContext>
        </ThemeProvider>
      </MsalAuthenticationTemplate>
    </MsalProvider>
  );
}

export default App;
