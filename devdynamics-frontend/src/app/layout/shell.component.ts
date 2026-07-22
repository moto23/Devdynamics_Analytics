import { Component, HostListener, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { IconComponent } from '../shared/icon.component';
import { ToastHostComponent } from '../shared/toast-host.component';
import { ThemeService } from '../core/theme.service';
import { provideGraphQL } from '../core/apollo.providers';
import { GraphqlService } from '../services/graphql.service';
import { AdminService } from '../core/admin.service';

interface NavItem {
  path: string;
  label: string;
  icon: string;
}

/**
 * Application shell: sidebar navigation, header, and the routed view.
 *
 * The sidebar is navigation only — filters live in the page header so it is
 * visible that they apply across the view rather than to one chart.
 */
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, IconComponent, ToastHostComponent],
  // Provided here rather than at the root so Apollo ships in this lazy chunk,
  // keeping it out of the landing page's initial download.
  providers: [provideGraphQL(), GraphqlService, AdminService],
  templateUrl: './shell.component.html',
  styleUrls: ['./shell.component.css']
})
export class ShellComponent {

  private readonly themeService = inject(ThemeService);

  readonly theme = this.themeService.theme;

  /** Icon-rail mode on desktop. */
  readonly collapsed = signal(false);

  /** Off-canvas drawer on small screens. */
  readonly drawerOpen = signal(false);

  readonly navItems: NavItem[] = [
    { path: '/dashboard',    label: 'Overview',     icon: 'grid'  },
    { path: '/repositories', label: 'Repositories', icon: 'repo'  },
    { path: '/contributors', label: 'Contributors', icon: 'users' },
    { path: '/settings',     label: 'Settings',     icon: 'settings' }
  ];

  toggleTheme() {
    this.themeService.toggle();
  }

  toggleSidebar() {
    if (window.innerWidth < 768) {
      this.drawerOpen.update(open => !open);
    } else {
      this.collapsed.update(value => !value);
    }
  }

  closeDrawer() {
    this.drawerOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  onEscape() {
    this.closeDrawer();
  }

  @HostListener('window:resize')
  onResize() {
    if (window.innerWidth >= 768 && this.drawerOpen()) {
      this.drawerOpen.set(false);
    }
  }
}
