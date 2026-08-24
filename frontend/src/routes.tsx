import { createBrowserRouter } from 'react-router-dom';
import { RequireAuth } from './auth/RequireAuth';
import { AppShell } from './shell/AppShell';
import { OverviewPage } from './features/overview/OverviewPage';
import { NotFoundPage } from './features/overview/NotFoundPage';
import { SignInPage } from './features/auth/SignInPage';
import { SignUpPage } from './features/auth/SignUpPage';
import { FlagsPage } from './features/flags/FlagsPage';
import { FlagDetailPage } from './features/flags/FlagDetailPage';
import { SegmentDetailPage } from './features/segments/SegmentDetailPage';
import { SegmentsPage } from './features/segments/SegmentsPage';
import { RulesPage } from './features/rules/RulesPage';
import { MembersPage } from './features/organization/MembersPage';
import { EnvironmentsPage } from './features/organization/EnvironmentsPage';
import { SettingsPage } from './features/organization/SettingsPage';

export const router = createBrowserRouter([
  // The auth screens sit outside the shell: there is no rail, no environment, and nothing to
  // navigate to until you have signed in.
  { path: '/sign-in', element: <SignInPage /> },
  { path: '/sign-up', element: <SignUpPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <OverviewPage /> },
          { path: 'flags', element: <FlagsPage /> },
          { path: 'flags/:key', element: <FlagDetailPage /> },
          { path: 'segments', element: <SegmentsPage /> },
          { path: 'segments/:key', element: <SegmentDetailPage /> },
          { path: 'rules', element: <RulesPage /> },
          { path: 'organization/members', element: <MembersPage /> },
          { path: 'organization/environments', element: <EnvironmentsPage /> },
          { path: 'organization/settings', element: <SettingsPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]);
