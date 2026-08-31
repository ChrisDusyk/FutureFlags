import type { ReactNode } from 'react';

import { Mark } from '../../shell/Mark';

/**
 * The frame both auth screens sit in. Deliberately not the AppShell: there is no rail and no
 * environment spine here, because before you have signed in there is no working environment to
 * be in — showing one would claim a context you do not yet have.
 */
export function AuthFrame({
  title,
  lede,
  children,
  footer,
}: {
  title: string;
  lede: string;
  children: ReactNode;
  footer: ReactNode;
}) {
  return (
    <main className="authpage" id="content">
      <section className="authcard">
        <div className="authcard__brand">
          <Mark size={22} />
          <span className="brand__name">
            FutureFlags
            <span className="brand__role">Console</span>
          </span>
        </div>

        <h1 className="authcard__title">{title}</h1>
        <p className="authcard__lede">{lede}</p>

        {children}

        <p className="authcard__foot">{footer}</p>
      </section>
    </main>
  );
}
