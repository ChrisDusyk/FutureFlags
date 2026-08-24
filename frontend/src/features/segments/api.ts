/**
 * The console's half of the segments API.
 *
 * Its own copy of ApiError, toApiError and send rather than a shared module, matching the flags and
 * organization slices — a slice owns its own wiring, and the duplication has stayed honest so far.
 */

import { apiFetch } from '../../auth/token';

/** A condition value is one of exactly the three types JSON has, because that is what the platform
 * stores and compares. There is no coercion between them anywhere: a condition written against the
 * number 30 never matches the string '30'. */
export type AttributeValue = string | number | boolean;

export type ConditionOperator =
  | 'equals'
  | 'one-of'
  | 'contains'
  | 'starts-with'
  | 'ends-with'
  | 'greater-than'
  | 'greater-than-or-equal'
  | 'less-than'
  | 'less-than-or-equal';

export interface SegmentCondition {
  attribute: string;
  operator: ConditionOperator;
  values: AttributeValue[];
}

export interface SegmentDefinition {
  includedKeys: string[];
  excludedKeys: string[];
  conditions: SegmentCondition[];
}

/** What the list screen gets: counts rather than the definition itself, since a list of twenty
 * would otherwise ship twenty condition sets to render three numbers. */
export interface SegmentSummary {
  id: string;
  key: string;
  name: string;
  description: string;
  conditionCount: number;
  includedKeyCount: number;
  excludedKeyCount: number;
  /** No included keys and no conditions — the one case guaranteed to match nobody, not the only
   * one a definition can. Worth saying out loud: an empty definition silently turns off every
   * flag that targets it. */
  isEmptyDefinition: boolean;
  createdAt: string;
  updatedAt: string;
}

interface ListSegmentsResponse {
  segments: SegmentSummary[];
}

/** One flag and environment that names this segment. */
export interface SegmentDependent {
  flagKey: string;
  flagName: string;
  environment: string;
}

export interface SegmentDetail {
  id: string;
  key: string;
  name: string;
  description: string;
  definition: SegmentDefinition;
  /** Everywhere this segment is currently holding something up. Editing it changes all of these
   * at once, and it cannot be deleted while the list is non-empty. */
  targetedBy: SegmentDependent[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateSegmentInput {
  key: string;
  name: string;
  description: string;
  definition: SegmentDefinition;
}

export interface UpdateSegmentInput {
  name: string;
  description: string;
  definition: SegmentDefinition;
}

export type SegmentHistoryEventType =
  | 'SegmentCreated'
  | 'SegmentDetailsChanged'
  | 'SegmentDefinitionChanged'
  | 'SegmentDeleted';

/**
 * One entry in a segment's activity log. `eventType` says which of the other fields are set:
 * name/description for SegmentCreated and SegmentDetailsChanged, definition for
 * SegmentDefinitionChanged, and neither for SegmentDeleted.
 */
export interface SegmentHistoryEntry {
  eventType: SegmentHistoryEventType;
  occurredAt: string;
  causedByName: string | null;
  name: string | null;
  description: string | null;
  definition: SegmentDefinition | null;
}

interface GetSegmentHistoryResponse {
  entries: SegmentHistoryEntry[];
}

/**
 * A failure the server explained. `code` is the domain error code, e.g. "Segment.StillTargeted".
 *
 * Fields are declared and assigned rather than taken as constructor parameter properties, which
 * `erasableSyntaxOnly` rules out — nothing here may depend on TypeScript emitting code.
 */
export class ApiError extends Error {
  code: string | null;
  status: number;

  constructor(message: string, code: string | null, status: number) {
    super(message);
    this.name = 'ApiError';
    this.code = code;
    this.status = status;
  }
}

const FALLBACK_MESSAGE = 'The console could not reach the API.';

async function toApiError(response: Response): Promise<ApiError> {
  try {
    const problem: unknown = await response.json();

    if (typeof problem === 'object' && problem !== null) {
      const { detail, code } = problem as { detail?: unknown; code?: unknown };

      return new ApiError(
        typeof detail === 'string' && detail.length > 0 ? detail : FALLBACK_MESSAGE,
        typeof code === 'string' ? code : null,
        response.status,
      );
    }
  } catch {
    // A response that is not JSON tells us nothing beyond its status, which is still worth having.
  }

  return new ApiError(FALLBACK_MESSAGE, null, response.status);
}

async function send(input: string, init: RequestInit = {}): Promise<Response> {
  let response: Response;

  try {
    response = await apiFetch(input, init);
  } catch (cause) {
    // An aborted request is the caller's own doing — let it through untouched so a screen
    // unmounting mid-flight does not paint an error on its way out.
    if (cause instanceof DOMException && cause.name === 'AbortError') {
      throw cause;
    }

    throw new ApiError(FALLBACK_MESSAGE, null, 0);
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  return response;
}

export async function listSegments(signal?: AbortSignal): Promise<SegmentSummary[]> {
  const response = await send('/api/segments', {
    signal,
    headers: { accept: 'application/json' },
  });

  const body = (await response.json()) as ListSegmentsResponse;

  return body.segments;
}

export async function getSegment(key: string, signal?: AbortSignal): Promise<SegmentDetail> {
  const response = await send(`/api/segments/${encodeURIComponent(key)}`, {
    signal,
    headers: { accept: 'application/json' },
  });

  return (await response.json()) as SegmentDetail;
}

export async function createSegment(input: CreateSegmentInput): Promise<void> {
  await send('/api/segments', {
    method: 'POST',
    headers: { 'content-type': 'application/json', accept: 'application/json' },
    body: JSON.stringify(input),
  });
}

/** Replaces a segment's details and definition together. They are edited on one screen and saved
 * by one button; splitting them would make a half-applied edit representable. */
export async function updateSegment(key: string, input: UpdateSegmentInput): Promise<void> {
  await send(`/api/segments/${encodeURIComponent(key)}`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json', accept: 'application/json' },
    body: JSON.stringify(input),
  });
}

/** Retires a segment. Refused while any flag targets it, in any environment — the error names them. */
export async function deleteSegment(key: string): Promise<void> {
  await send(`/api/segments/${encodeURIComponent(key)}`, { method: 'DELETE' });
}

export async function getSegmentHistory(
  key: string,
  signal?: AbortSignal,
): Promise<SegmentHistoryEntry[]> {
  const response = await send(`/api/segments/${encodeURIComponent(key)}/history`, {
    signal,
    headers: { accept: 'application/json' },
  });

  const body = (await response.json()) as GetSegmentHistoryResponse;

  return body.entries;
}

export const EMPTY_DEFINITION: SegmentDefinition = {
  includedKeys: [],
  excludedKeys: [],
  conditions: [],
};
