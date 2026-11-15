import { MantineProvider } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router';
import { AppLayout } from './layout/AppLayout';
import { features } from './features/registry';
import { theme } from './theme';

// Authentication will wrap AppLayout (e.g. a <RequireAuth> element) once login is added.
const router = createBrowserRouter([
  {
    path: '/',
    element: <AppLayout />,
    children: [
      { index: true, element: <Navigate to={features[0].path} replace /> },
      ...features.map((f) => ({ path: f.path, element: <f.element /> })),
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
]);

export function App() {
  return (
    <MantineProvider theme={theme} defaultColorScheme="light">
      <Notifications position="top-right" />
      <RouterProvider router={router} />
    </MantineProvider>
  );
}
