import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { IconComponent } from '../shared/icon.component';
import { StatusBadgeComponent } from '../shared/table.components';
import { ThemeService } from '../core/theme.service';
import { AdminService } from '../core/admin.service';
import { ToastService } from '../core/toast.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, StatusBadgeComponent],
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.css']
})
export class SettingsComponent {

  private readonly themeService = inject(ThemeService);
  private readonly toast = inject(ToastService);

  readonly admin = inject(AdminService);
  readonly theme = this.themeService.theme;

  readonly apiUrl = environment.graphqlUri;

  keyInput = '';
  readonly verifying = signal(false);
  readonly error = signal<string | null>(null);

  setTheme(value: 'dark' | 'light') {
    this.themeService.set(value);
  }

  async unlock() {
    this.error.set(null);
    this.verifying.set(true);

    // Validated against the API before storing, so an incorrect key is rejected
    // here rather than surfacing later as a failed action.
    const result = await this.admin.signIn(this.keyInput);

    this.verifying.set(false);

    if (result.ok) {
      this.keyInput = '';
      this.toast.success('Administration unlocked', 'Management actions are now available.');
    } else {
      this.error.set(result.message);
    }
  }

  lock() {
    this.admin.signOut();
    this.toast.info('Administration locked', 'Management actions are hidden again.');
  }
}
