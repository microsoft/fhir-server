
import { Stack } from '@fluentui/react';

import { useAppContext } from '../AppContext';
import { UserInfo } from '../components/UserInfo'
import { AppInfo } from '../components/AppInfo'
import { ScopeSelector } from '../components/ScopeSelector'


export default function Welcome() {
    const app = useAppContext();

    return (
        <Stack>
            <UserInfo user={app.user}/>
            <AppInfo appInfo={app.application} />
            <ScopeSelector 
                appScopes={app.application?.requiredResourceAccess}
                currentlyApprovedScopes={app.currentlyApprovedScopes}
                requestedScopes={app.authInfo?.scope}
            />
        </Stack>
    );
}