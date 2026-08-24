import { useId } from 'react';

import type { AttributeValue, SegmentCondition } from './api';
import {
  blankFor,
  KIND_LABELS,
  kindOf,
  operatorFor,
  OPERATORS,
  type ValueKind,
} from './conditionOperators';

interface ConditionEditorProps {
  condition: SegmentCondition;
  index: number;
  onChange: (condition: SegmentCondition) => void;
  onRemove: () => void;
}

/**
 * One row of *attribute · comparison · value*.
 *
 * The type of the value is chosen explicitly rather than guessed from what was typed. A console
 * that let somebody type `18` into a text box and stored `"18"` would produce a segment that looks
 * right, saves cleanly, and matches nobody — the one failure that gives no sign of itself.
 */
export function ConditionEditor({ condition, index, onChange, onRemove }: ConditionEditorProps) {
  const id = useId();
  const chosen = operatorFor(condition.operator);
  const kind = kindOf(condition.values[0]);

  function changeOperator(value: string) {
    const next = operatorFor(value);

    // An operator that cannot compare the current type takes the first type it can, so the row is
    // never briefly in a state the server would refuse.
    const nextKind = next.accepts.includes(kind) ? kind : next.accepts[0]!;
    const values = next.multiValued ? condition.values : condition.values.slice(0, 1);

    onChange({
      ...condition,
      operator: next.value,
      values: (values.length > 0 ? values : [blankFor(nextKind)]).map((value) =>
        kindOf(value) === nextKind ? value : blankFor(nextKind),
      ),
    });
  }

  function changeKind(nextKind: ValueKind) {
    onChange({ ...condition, values: condition.values.map(() => blankFor(nextKind)) });
  }

  function changeValue(at: number, raw: AttributeValue) {
    onChange({
      ...condition,
      values: condition.values.map((value, position) => (position === at ? raw : value)),
    });
  }

  return (
    <li className="condition">
      <div className="condition__row">
        <label className="condition__field">
          <span className="condition__label" id={`${id}-attribute`}>
            Attribute
          </span>
          <input
            className="condition__input"
            aria-labelledby={`${id}-attribute`}
            value={condition.attribute}
            placeholder="plan"
            onChange={(event) => onChange({ ...condition, attribute: event.target.value })}
          />
        </label>

        <label className="condition__field">
          <span className="condition__label" id={`${id}-operator`}>
            Comparison
          </span>
          <select
            className="condition__input"
            aria-labelledby={`${id}-operator`}
            value={condition.operator}
            onChange={(event) => changeOperator(event.target.value)}
          >
            {OPERATORS.map((operator) => (
              <option key={operator.value} value={operator.value}>
                {operator.label}
              </option>
            ))}
          </select>
        </label>

        <label className="condition__field condition__field--kind">
          <span className="condition__label" id={`${id}-kind`}>
            Type
          </span>
          <select
            className="condition__input"
            aria-labelledby={`${id}-kind`}
            value={kind}
            disabled={chosen.accepts.length === 1}
            onChange={(event) => changeKind(event.target.value as ValueKind)}
          >
            {chosen.accepts.map((accepted) => (
              <option key={accepted} value={accepted}>
                {KIND_LABELS[accepted]}
              </option>
            ))}
          </select>
        </label>

        <button
          type="button"
          className="condition__remove"
          onClick={onRemove}
          aria-label={`Remove condition ${index + 1}`}
        >
          Remove
        </button>
      </div>

      <ul className="condition__values">
        {condition.values.map((value, position) => (
          <li key={position} className="condition__value">
            <ValueInput
              kind={kind}
              value={value}
              label={`Condition ${index + 1} value ${position + 1}`}
              onChange={(next) => changeValue(position, next)}
            />
            {chosen.multiValued && condition.values.length > 1 && (
              <button
                type="button"
                className="condition__remove"
                aria-label={`Remove value ${position + 1} of condition ${index + 1}`}
                onClick={() =>
                  onChange({
                    ...condition,
                    values: condition.values.filter((_, at) => at !== position),
                  })
                }
              >
                Remove
              </button>
            )}
          </li>
        ))}

        {chosen.multiValued && (
          <li>
            <button
              type="button"
              className="textlink"
              onClick={() =>
                onChange({ ...condition, values: [...condition.values, blankFor(kind)] })
              }
            >
              Add another value
            </button>
          </li>
        )}
      </ul>
    </li>
  );
}

interface ValueInputProps {
  kind: ValueKind;
  value: AttributeValue;
  label: string;
  onChange: (value: AttributeValue) => void;
}

function ValueInput({ kind, value, label, onChange }: ValueInputProps) {
  if (kind === 'boolean') {
    return (
      <select
        className="condition__input"
        aria-label={label}
        value={value === true ? 'true' : 'false'}
        onChange={(event) => onChange(event.target.value === 'true')}
      >
        <option value="true">true</option>
        <option value="false">false</option>
      </select>
    );
  }

  if (kind === 'number') {
    return (
      <input
        className="condition__input"
        type="number"
        aria-label={label}
        value={typeof value === 'number' ? value : ''}
        onChange={(event) => {
          const parsed = Number(event.target.value);

          // An unparseable box stays at zero rather than becoming NaN. NaN has no JSON
          // representation, so it could never reach the server intact anyway.
          onChange(event.target.value.length > 0 && Number.isFinite(parsed) ? parsed : 0);
        }}
      />
    );
  }

  return (
    <input
      className="condition__input"
      aria-label={label}
      value={typeof value === 'string' ? value : ''}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}
