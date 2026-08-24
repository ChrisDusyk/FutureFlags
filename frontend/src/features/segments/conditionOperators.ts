import type { AttributeValue, ConditionOperator, SegmentCondition } from './api';

/**
 * Which value types an operator can compare, and whether more than one value is meaningful.
 *
 * It mirrors `ConditionOperator` on the server, which refuses a mismatch outright — a condition
 * comparing a number against text would otherwise be saved and then silently match nobody, which is
 * the worst failure this type system can have.
 *
 * In its own module rather than beside the component so that the component file exports only a
 * component, which is what keeps fast refresh working.
 */
export type ValueKind = 'text' | 'number' | 'boolean';

export interface OperatorChoice {
  value: ConditionOperator;
  label: string;
  accepts: ValueKind[];
  multiValued: boolean;
}

export const OPERATORS: OperatorChoice[] = [
  { value: 'equals', label: 'is', accepts: ['text', 'number', 'boolean'], multiValued: false },
  { value: 'one-of', label: 'is one of', accepts: ['text', 'number', 'boolean'], multiValued: true },
  { value: 'contains', label: 'contains', accepts: ['text'], multiValued: false },
  { value: 'starts-with', label: 'starts with', accepts: ['text'], multiValued: false },
  { value: 'ends-with', label: 'ends with', accepts: ['text'], multiValued: false },
  { value: 'greater-than', label: 'is more than', accepts: ['number'], multiValued: false },
  {
    value: 'greater-than-or-equal',
    label: 'is at least',
    accepts: ['number'],
    multiValued: false,
  },
  { value: 'less-than', label: 'is less than', accepts: ['number'], multiValued: false },
  { value: 'less-than-or-equal', label: 'is at most', accepts: ['number'], multiValued: false },
];

export const KIND_LABELS: Record<ValueKind, string> = {
  text: 'text',
  number: 'number',
  boolean: 'true/false',
};

export function operatorFor(value: string): OperatorChoice {
  return OPERATORS.find((candidate) => candidate.value === value) ?? OPERATORS[0]!;
}

export function kindOf(value: AttributeValue | undefined): ValueKind {
  if (typeof value === 'number') {
    return 'number';
  }

  return typeof value === 'boolean' ? 'boolean' : 'text';
}

/** The blank a condition falls back to when its type changes, so the input always holds something
 * of the right type rather than a value the server would refuse. */
export function blankFor(kind: ValueKind): AttributeValue {
  if (kind === 'number') {
    return 0;
  }

  return kind === 'boolean' ? true : '';
}

export function blankCondition(): SegmentCondition {
  return { attribute: '', operator: 'equals', values: [''] };
}
