import React, { useEffect, useState, FC, ReactElement } from 'react';
import { Link, Stack, Text, } from '@fluentui/react';

import { AppUser } from '../AppContext'

interface UserInfoProps {
    user?: AppUser
    logout?: Function
}

export const UserInfo: FC<UserInfoProps> = ( props: UserInfoProps): ReactElement => {
    const [name, setName] = useState(props.user?.displayName);
    const [email, setEmail] = useState(props.user?.email);

    useEffect(() => {
        setName(props.user?.displayName || '');
        setEmail(props.user?.email || '');
    }, [props]);

    return (
        <Stack>
            <Stack.Item>
                <Text block variant="xLarge">Welcome {name}!</Text>
                <Text variant="small">{email}</Text>
                <Text variant="small">Please review the below access permissions before continuing.</Text>
            </Stack.Item>
            <Stack.Item>
                <Link variant="small" onClick={() => props.logout?.call(null)}>Logout</Link>
            </Stack.Item>

        </Stack>
    )
}

export default UserInfo;