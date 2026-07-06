import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DxDataGridModule } from 'devextreme-angular';
import { ClinicalRecordRow } from '../../models/triallens.models';

@Component({
  selector: 'x2g-triallens-records-grid',
  standalone: true,
  imports: [DxDataGridModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dx-data-grid
      [dataSource]="clinicalRecords()"
      keyExpr="mrn"
      [remoteOperations]="true"
      [showBorders]="true"
      [columnAutoWidth]="true"
      [allowColumnReordering]="true"
    >
      <dxo-scrolling mode="virtual" rowRenderingMode="virtual"></dxo-scrolling>
      <dxo-column-chooser [enabled]="true"></dxo-column-chooser>

      <!-- dataType="string" + allowEditing=false: the MRN column must never be
           reinterpreted as numeric by grid sorting/filtering or inline edits
           (Section 3.2 override — leading zeros and alpha chars are load-bearing). -->
      <dxi-column
        dataField="mrn"
        caption="MRN"
        dataType="string"
        [allowEditing]="false"
      ></dxi-column>
      <dxi-column dataField="dataset_id" caption="Dataset" dataType="string"></dxi-column>
      <dxi-column dataField="protocol_id" caption="Protocol" dataType="string"></dxi-column>
      <dxi-column dataField="record_hash" caption="Record Hash" dataType="string"></dxi-column>
      <dxi-column
        dataField="intake_timestamp"
        caption="Intake"
        dataType="datetime"
      ></dxi-column>
      <dxi-column dataField="pipeline_state" caption="State" dataType="string"></dxi-column>
    </dx-data-grid>
  `,
})
export class TrialLensRecordsGridComponent {
  readonly clinicalRecords = input.required<ClinicalRecordRow[]>();
}
