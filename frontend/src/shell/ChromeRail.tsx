import { NavLink } from 'react-router-dom';
import type { CSSProperties } from 'react';
import { EnvironmentSwitcher } from './EnvironmentSwitcher';
import { Mark } from './Mark';
import { navItemIndex, navigation } from './navigation';
import { SignedInAs } from './SignedInAs';

/** The dark rail: brand, sections, and nothing that competes with the spine. */
export function ChromeRail({ id, open }: { id: string; open: boolean }) {
  return (
    <aside id={id} className={open ? 'shell__chrome shell__chrome--open' : 'shell__chrome'}>
      <div className="brand">
        <Mark />
        <span className="brand__name">
          FutureFlags
          <span className="brand__role">Console</span>
        </span>
      </div>

      <EnvironmentSwitcher />

      <nav className="nav" aria-label="Console sections">
        {navigation.map((section) => (
          <div key={section.id}>
            {section.label && <h2 className="nav__label">{section.label}</h2>}
            <ul>
              {section.items.map((item) => (
                <li key={item.to}>
                  <NavLink
                    to={item.to}
                    end={item.to === '/'}
                    style={{ '--index': navItemIndex.get(item.to) ?? 0 } as CSSProperties}
                    className={({ isActive }) =>
                      isActive ? 'nav__link nav__link--active' : 'nav__link'
                    }
                  >
                    {item.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <div className="chrome__foot">
        <SignedInAs />
      </div>
    </aside>
  );
}
