import type { SegmentDefinition } from './api';
import { ConditionEditor } from './ConditionEditor';
import { blankCondition } from './conditionOperators';

interface DefinitionEditorProps {
  definition: SegmentDefinition;
  onChange: (definition: SegmentDefinition) => void;
  disabled?: boolean;
}

/**
 * Who is in a segment: two lists of keys, and conditions that all have to hold.
 *
 * The reading order is stated on screen rather than left to be discovered, because it is the part
 * people get wrong — excluded beats included beats conditions, and a segment with nothing in it
 * matches nobody rather than everybody.
 */
export function DefinitionEditor({ definition, onChange, disabled }: DefinitionEditorProps) {
  const isEmptyDefinition =
    definition.includedKeys.length === 0 && definition.conditions.length === 0;

  return (
    <fieldset className="definition" disabled={disabled}>
      <legend className="definition__legend">Who is in it</legend>

      <ol className="definition__order">
        <li>Anyone in the excluded list is out, whatever else says so.</li>
        <li>Anyone in the included list is in, without having to match a condition.</li>
        <li>Everyone else is in only if <em>every</em> condition holds.</li>
      </ol>

      <label className="field">
        <span className="field__label">Included keys</span>
        <span className="field__hint">
          One per line — the context&rsquo;s <strong>key</strong>, not one of the traits under
          Conditions below: the identifier your app passes for who it is asking about, such as a
          user id or an account id. This is how you reach one account directly, like a person you
          are debugging. Compared exactly, so casing matters, and only a context that actually
          names a key can match here — one built from attributes alone never will.
        </span>
        <textarea
          className="field__input"
          rows={3}
          placeholder={'user-42\nacct-9f2a'}
          value={definition.includedKeys.join('\n')}
          onChange={(event) =>
            onChange({ ...definition, includedKeys: toLines(event.target.value) })
          }
        />
      </label>

      <label className="field">
        <span className="field__label">Excluded keys</span>
        <span className="field__hint">
          Same format as included keys, one per line — usually because something is broken for
          this person specifically. This beats everything else, including a matching condition, so
          it stays reliable exactly when it is needed most.
        </span>
        <textarea
          className="field__input"
          rows={3}
          placeholder="user-17"
          value={definition.excludedKeys.join('\n')}
          onChange={(event) =>
            onChange({ ...definition, excludedKeys: toLines(event.target.value) })
          }
        />
      </label>

      <div className="definition__conditions">
        <span className="field__label">Conditions</span>
        <span className="field__hint">
          Matched against the traits your app sends when it evaluates a flag. All of them have to
          hold.
        </span>

        {definition.conditions.length > 0 && (
          <ul className="conditions">
            {definition.conditions.map((condition, index) => (
              <ConditionEditor
                key={index}
                condition={condition}
                index={index}
                onChange={(next) =>
                  onChange({
                    ...definition,
                    conditions: definition.conditions.map((existing, at) =>
                      at === index ? next : existing,
                    ),
                  })
                }
                onRemove={() =>
                  onChange({
                    ...definition,
                    conditions: definition.conditions.filter((_, at) => at !== index),
                  })
                }
              />
            ))}
          </ul>
        )}

        <button
          type="button"
          className="button button--quiet"
          onClick={() =>
            onChange({ ...definition, conditions: [...definition.conditions, blankCondition()] })
          }
        >
          Add a condition
        </button>
      </div>

      {/*
        Said here, while it can still be fixed, rather than left to be noticed as a flag that went
        quiet. A segment nobody can be in turns off every flag that targets it.
      */}
      {isEmptyDefinition && (
        <p className="definition__warning" role="status">
          As written, nobody is in this segment — every flag that targets it will be off. Add an
          included key or a condition.
        </p>
      )}
    </fieldset>
  );
}

/** Blank lines are dropped rather than stored: the server drops them too, and a textarea that
 * silently disagreed with what was saved would be its own small lie. */
function toLines(value: string): string[] {
  return value
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}
