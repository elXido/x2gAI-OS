import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DxDataGridModule } from 'devextreme-angular';
import { FreightTicketRow } from '../../models/xtms.models';

@Component({
  selector: 'x2g-xtms-freight-grid',
  standalone: true,
  imports: [DxDataGridModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dx-data-grid
      [dataSource]="freightTickets()"
      keyExpr="ticket_id"
      [remoteOperations]="true"
      [showBorders]="true"
      [columnAutoWidth]="true"
      [allowColumnReordering]="true"
    >
      <dxo-scrolling mode="virtual" rowRenderingMode="virtual"></dxo-scrolling>
      <dxo-column-chooser [enabled]="true"></dxo-column-chooser>

      <dxi-column dataField="ticket_id" caption="Ticket" dataType="string"></dxi-column>
      <dxi-column dataField="freight_id" caption="Freight ID" dataType="string"></dxi-column>
      <dxi-column dataField="ratecon.ratecon_id" caption="Ratecon" dataType="string"></dxi-column>
      <dxi-column dataField="ratecon.ratecon_origin" caption="Origin" dataType="string"></dxi-column>
      <dxi-column dataField="ratecon.ratecon_destination" caption="Destination" dataType="string"></dxi-column>
      <dxi-column
        dataField="ratecon.ratecon_rate_usd"
        caption="Rate (USD)"
        dataType="number"
        format="currency"
      ></dxi-column>
      <dxi-column
        dataField="ratecon.freight_weight_lbs"
        caption="Weight (lbs)"
        dataType="number"
      ></dxi-column>
      <dxi-column dataField="ratecon.ratecon_pickup_window" caption="Pickup Window" dataType="string"></dxi-column>
      <dxi-column dataField="ratecon.ratecon_delivery_window" caption="Delivery Window" dataType="string"></dxi-column>
      <dxi-column dataField="ratecon.ratecon_carrier_mc" caption="Carrier MC" dataType="string"></dxi-column>
      <dxi-column dataField="pipeline_state" caption="State" dataType="string"></dxi-column>
      <dxi-column dataField="sms_dispatched" caption="SMS" dataType="boolean"></dxi-column>
    </dx-data-grid>
  `,
})
export class XtmsFreightGridComponent {
  readonly freightTickets = input.required<FreightTicketRow[]>();
}
