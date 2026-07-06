import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { DxTreeViewModule } from 'devextreme-angular';
import type { ItemClickEvent } from 'devextreme/ui/tree_view';
import { TelemetryService } from './core/telemetry.service';
import { MenuNode } from './models/ws.models';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, DxTreeViewModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly telemetry = inject(TelemetryService);
  private readonly router = inject(Router);

  protected onMenuItemClick(event: ItemClickEvent): void {
    const node = event.itemData as MenuNode | undefined;
    if (node?.route) {
      void this.router.navigateByUrl(node.route);
    }
  }
}
