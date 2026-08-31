import { betterAuth } from 'better-auth';
import { admin } from 'better-auth/plugins/admin';
import { jwt } from 'better-auth/plugins/jwt';

import {
  authSchema,
  baseUrl,
  jwtAudience,
  jwtIssuer,
  secret,
  trustedOrigins,
} from './config.ts';
import { pool } from './db.ts';
import { uuidv7 } from './uuid.ts';

/** The two roles the console knows about. `user` is the ordinary experience. */
export const roles = ['user', 'admin'] as const;

export const auth = betterAuth({
  appName: 'FutureFlags',
  secret,
  baseURL: baseUrl,
  // The browser only ever reaches this service through the .NET server's forwarder,
  // so the paths Better Auth generates have to be written from that origin's point of view.
  basePath: '/api/auth',
  trustedOrigins,
  database: pool,

  emailAndPassword: {
    enabled: true,
    // No mail provider is wired up yet, so demanding a verified address would lock
    // everyone out. See `sendResetPassword` below for the shape that has to be filled in.
    requireEmailVerification: false,
    minPasswordLength: 12,
    sendResetPassword: async ({ url, user }) => {
      // Deliberately a log line rather than a silent no-op: password reset is reachable
      // from the sign-in screen, and in development the link has to go somewhere.
      console.info(`[auth] password reset for ${user.email}: ${url}`);
    },
  },

  advanced: {
    database: {
      generateId: () => uuidv7(),
    },
  },

  databaseHooks: {
    user: {
      create: {
        before: async (user) => {
          // Somebody has to be able to administer a fresh install, and there is no
          // seeding step: the first account to exist owns it. Two simultaneous
          // sign-ups against an empty database could both win this check, which is
          // an acceptable trade for not shipping a bootstrap credential.
          const existing = await pool.query(`SELECT 1 FROM "${authSchema}"."user" LIMIT 1`);

          return {
            data: {
              ...user,
              role: existing.rowCount === 0 ? 'admin' : 'user',
            },
          };
        },
      },
    },
  },

  plugins: [
    admin({
      defaultRole: 'user',
      adminRoles: ['admin'],
    }),
    jwt({
      jwks: {
        // Better Auth defaults to EdDSA, which Microsoft.IdentityModel cannot validate.
        // ES256 is the strongest algorithm both sides agree on.
        keyPairConfig: { alg: 'ES256' },
      },
      jwt: {
        issuer: jwtIssuer,
        audience: jwtAudience,
        expirationTime: '15m',
        // Only what the API authorizes on. `sub` carries the user id already.
        definePayload: ({ user }) => ({
          email: user.email,
          name: user.name,
          role: user.role ?? 'user',
        }),
      },
    }),
  ],
});
