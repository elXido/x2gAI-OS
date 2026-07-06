import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { DxButtonModule } from 'devextreme-angular';
import { TelemetryService } from '../core/telemetry.service';

@Component({
  selector: 'x2g-dashboard',
  imports: [DxButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>System Overview</h2>
    <dl class="stats">
      <div class="stat">
        <dt>Telemetry channel</dt>
        <dd>{{ telemetry.connectionState() }}</dd>
      </div>
      <div class="stat">
        <dt>Discovered modules</dt>
        <dd>{{ telemetry.menuNodes().length }}</dd>
      </div>
      <div class="stat">
        <dt>Last pipeline event</dt>
        <dd>
          @if (telemetry.lastTelemetry(); as event) {
            {{ event.pipeline }} → {{ event.state }}
          } @else {
            none yet
          }
        </dd>
      </div>
    </dl>
    <dx-button
      text="Rescan modules"
      [disabled]="rescanning() || !telemetry.isConnected()"
      (onClick)="rescan()"
    />
    @if (scanMessage(); as message) {
      <p class="scan-message">{{ message }}</p>
    }
  `,
  styles: `
    .stats {
      display: flex;
      gap: 24px;
      margin: 16px 0 24px;
    }
    .stat dt {
      font-size: 12px;
      color: #666;
    }
    .stat dd {
      margin: 4px 0 0;
      font-size: 20px;
    }
    .scan-message {
      margin-top: 12px;
      font-size: 13px;
      color: #666;
    }
  `,
})
export class Dashboard {
  protected readonly telemetry = inject(TelemetryService);
  protected readonly rescanning = signal(false);
  protected readonly scanMessage = signal<string | null>(null);

  protected async rescan(): Promise<void> {
    this.rescanning.set(true);
    try {
      const result = await this.telemetry.request('discover-now', undefined);
      this.scanMessage.set(`Discovery pass complete: ${result.moduleCount} modules.`);
    } catch (error) {
      this.scanMessage.set(error instanceof Error ? error.message : 'discovery request failed');
    } finally {
      this.rescanning.set(false);
    }
  }
}
