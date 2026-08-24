import { useEffect, useRef, useState } from 'react';

import { ApiError, createSegment, EMPTY_DEFINITION, type SegmentDefinition } from './api';
import { DefinitionEditor } from './DefinitionEditor';

interface NewSegmentDialogProps {
  onClose: () => void;
  onCreated: () => void;
}

export function NewSegmentDialog({ onClose, onCreated }: NewSegmentDialogProps) {
  const dialog = useRef<HTMLDialogElement>(null);

  const [key, setKey] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [definition, setDefinition] = useState<SegmentDefinition>(EMPTY_DEFINITION);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // showModal rather than the open attribute: it is what brings the focus trap, Escape, and an
  // inert background with it.
  useEffect(() => dialog.current?.showModal(), []);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);

    try {
      await createSegment({ key, name, description, definition });
      onCreated();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : 'The segment could not be created.');
      setSaving(false);
    }
  }

  return (
    <dialog className="dialog dialog--wide" ref={dialog} onClose={onClose}>
      <form className="dialog__form" onSubmit={submit}>
        <h2 className="dialog__title">New segment</h2>

        <label className="field">
          <span className="field__label">Key</span>
          <span className="field__hint">
            Lowercase, hyphenated — <code>beta-testers</code>. It cannot be changed later, and is
            never reused once a segment is deleted.
          </span>
          <input
            className="field__input"
            value={key}
            onChange={(event) => setKey(event.target.value)}
            required
          />
        </label>

        <label className="field">
          <span className="field__label">Name</span>
          <input
            className="field__input"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />
        </label>

        <label className="field">
          <span className="field__label">Description</span>
          <span className="field__hint">What this group is, for whoever reads it next.</span>
          <input
            className="field__input"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
          />
        </label>

        <DefinitionEditor definition={definition} onChange={setDefinition} disabled={saving} />

        {error !== null && (
          <p className="field__error" role="alert">
            {error}
          </p>
        )}

        <div className="dialog__actions">
          <button
            type="button"
            className="button button--quiet"
            onClick={() => dialog.current?.close()}
            disabled={saving}
          >
            Cancel
          </button>
          <button type="submit" className="button" disabled={saving}>
            {saving ? 'Creating…' : 'Create segment'}
          </button>
        </div>
      </form>
    </dialog>
  );
}
