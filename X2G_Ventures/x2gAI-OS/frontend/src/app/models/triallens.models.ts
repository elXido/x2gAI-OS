/**
 * TrialLens wire models — SECTION 3.2 OVERRIDE.
 * MRNs are opaque external identifiers (leading zeros, alpha prefixes).
 * The branded type below makes a plain number unassignable at compile time,
 * and toMedicalRecordNumber() is the single runtime entry point — it rejects
 * non-string input outright rather than coercing, because a numeric parse
 * upstream has already destroyed leading zeros irreversibly.
 */

export type MedicalRecordNumber = string & { readonly __mrnBrand: unique symbol };

export const MRN_PATTERN = /^[A-Za-z0-9._-]{1,64}$/;

export function toMedicalRecordNumber(raw: unknown): MedicalRecordNumber {
  if (typeof raw !== 'string') {
    throw new TypeError(
      'MRN rejected: value is not a string; numeric coercion of MRNs is forbidden (Section 3.2 override).'
    );
  }
  if (!MRN_PATTERN.test(raw)) {
    throw new TypeError(
      'MRN rejected: contains characters outside [A-Za-z0-9._-] or exceeds 64 characters.'
    );
  }
  return raw as MedicalRecordNumber;
}

/** Raw identifiers must never reach logs; this is the only loggable form. */
export function redactMrn(mrn: MedicalRecordNumber): string {
  return mrn.length <= 4 ? '****' : `****${mrn.slice(-4)}`;
}

export type TrialLensPipelineState =
  | 'INTAKE_DATASET'
  | 'NORMALIZE_RECORDS'
  | 'MAP_PROTOCOL'
  | 'INDEX_MEMORY'
  | 'REPORT';

export interface ClinicalRecordRow {
  mrn: MedicalRecordNumber;
  dataset_id: string;
  protocol_id: string;
  record_hash: string;
  intake_timestamp: string;
  pipeline_state: TrialLensPipelineState;
}
