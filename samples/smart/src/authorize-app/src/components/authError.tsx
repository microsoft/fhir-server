import React, { useEffect, useState, FC, ReactElement } from 'react';
import { MsalAuthenticationResult } from "@azure/msal-react";
import { Stack, Text, } from '@fluentui/react';


export const AuthError: FC<MsalAuthenticationResult> = ( props: MsalAuthenticationResult): ReactElement => {
    const [name, setName] = useState(props.error?.name);
    const [message, setMessage] = useState(props.error?.message);

    useEffect(() => {
        setName(props.error?.name || '');
        setMessage(props.error?.message || '');
    }, [props]);

    return (
        <Stack>
            <Stack.Item>
            <Text block variant="xLarge">Error: {name}</Text>
                <Text variant="small">{message}</Text>
            </Stack.Item>
        </Stack>
    )
}

export default AuthError;