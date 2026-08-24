import { Link } from 'react-router-dom';
import type { CSSProperties } from 'react';
import { PageHeader } from '../../shell/PageHeader';
import { useEnvironment } from '../../shell/environment';
import { navigation } from '../../shell/navigation';

export function OverviewPage() {
  const { environment } = useEnvironment();
  const sections = navigation.filter((section) => section.label);

  return (
    <>
      <PageHeader
        eyebrow="Console"
        title="Overview"
        lede="Feature flags your app checks at runtime, and the people and rules behind them. The shell is here; the screens land one at a time."
      />

      <section
        className="envcard"
        style={{ '--tone': environment.tone } as CSSProperties}
        aria-label="Working environment"
      >
        <p className="envcard__tag">Working environment</p>
        <p className="envcard__name">
          {environment.name}
          <span className="envcard__key">{environment.key}</span>
        </p>
        <p className="envcard__blurb">{environment.blurb}</p>
        <p className="envcard__hint">
          Everything you read and change in this console applies to {environment.name}. Change it
          with the environment picker above the navigation.
        </p>
      </section>

      {sections.map((section) => (
        <section className="section" key={section.id}>
          <h2 className="section__label">{section.label}</h2>
          <div className="tiles">
            {section.items.map((item) => (
              <Link className="tile" key={item.to} to={item.to}>
                <span className="tile__name">
                  {item.label}
                  <span className="tile__arrow" aria-hidden="true">
                    →
                  </span>
                </span>
                <span className="tile__blurb">{item.blurb}</span>
                {item.built !== true && <span className="tile__state">Not built yet</span>}
              </Link>
            ))}
          </div>
        </section>
      ))}
    </>
  );
}
