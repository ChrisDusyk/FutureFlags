import { Link } from 'react-router-dom';

import { changedAgo, changedExactly } from '../flags/changed';
import type { SegmentSummary } from './api';

interface SegmentRowProps {
  segment: SegmentSummary;
}

export function SegmentRow({ segment }: SegmentRowProps) {
  return (
    <li className="seglist__row">
      <Link className="seglist__link" to={`/segments/${encodeURIComponent(segment.key)}`}>
        <span className="seglist__name">{segment.name}</span>
        <code className="seglist__key">{segment.key}</code>
      </Link>

      {segment.description.length > 0 && (
        <p className="seglist__description">{segment.description}</p>
      )}

      <p className="seglist__facts">
        <span>{describe(segment)}</span>
        <span className="seglist__changed" title={changedExactly(segment.updatedAt)}>
          {changedAgo(segment.updatedAt)}
        </span>
      </p>

      {/*
        A segment nobody can be in is not an empty segment — it silently turns off every flag that
        targets it, so the list says so rather than leaving three zeroes to be interpreted.
      */}
      {segment.isEmptyDefinition && (
        <p className="seglist__warning">Nobody is in this segment as written.</p>
      )}
    </li>
  );
}

function describe(segment: SegmentSummary): string {
  const parts: string[] = [];

  if (segment.conditionCount > 0) {
    parts.push(`${segment.conditionCount} ${segment.conditionCount === 1 ? 'condition' : 'conditions'}`);
  }

  if (segment.includedKeyCount > 0) {
    parts.push(`${segment.includedKeyCount} included`);
  }

  if (segment.excludedKeyCount > 0) {
    parts.push(`${segment.excludedKeyCount} excluded`);
  }

  return parts.length > 0 ? parts.join(' · ') : 'Nothing defined yet';
}
