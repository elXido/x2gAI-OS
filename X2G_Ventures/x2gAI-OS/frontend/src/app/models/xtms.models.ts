/**
 * X-TMS wire models — SECTION 3.1 OVERRIDE.
 * Field names bind 1:1 to xtms.pipeline.json recordSchemas and to the F#
 * records in src/Domain.fs. Canonical terminology is 'freight' / 'ratecon';
 * the generic synonyms 'load', 'rate_confirmation', and 'carrier_doc' are
 * schema violations and must never appear in dataFields or payload keys.
 */

export type XtmsPipelineState =
  | 'INGEST_ALERT'
  | 'PARSE_RATECON'
  | 'VALIDATE_FREIGHT'
  | 'DISPATCH_SMS'
  | 'ARCHIVE';

export interface FreightAlertRow {
  freight_id: string;
  freight_source: string;
  freight_received_at: string;
  ratecon_document_path: string;
}

export interface RateconRow {
  ratecon_id: string;
  freight_id: string;
  ratecon_origin: string;
  ratecon_destination: string;
  ratecon_rate_usd: number;
  freight_weight_lbs: number;
  ratecon_pickup_window: string;
  ratecon_delivery_window: string;
  ratecon_carrier_mc: string;
}

export interface FreightTicketRow {
  ticket_id: string;
  freight_id: string;
  ratecon: RateconRow;
  pipeline_state: XtmsPipelineState;
  sms_dispatched: boolean;
  audit_ref: string;
}
