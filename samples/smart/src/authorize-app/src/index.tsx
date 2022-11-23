import React from 'react';
import ReactDOM from 'react-dom/client';
import { mergeStyles } from '@fluentui/react';
import { App } from './App';

// Inject some global styles
mergeStyles({
  ':global(body,html,#root)': {
    //margin: 0,
    //padding: 0,
    height: '100vh',
  },
});

const root = ReactDOM.createRoot(
  document.getElementById('root') as HTMLElement
);

root.render(
    <App/>
);