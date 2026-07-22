import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { ThemeService } from './core/theme.service';

/**
 * Root shell. Layout lives in ShellComponent; this only mounts the router and
 * ensures the theme is applied before the first paint of any route.
 */
@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  template: '<router-outlet></router-outlet>',
  styles: [':host { display: block; }']
})
export class AppComponent {
  // Instantiating the service applies the persisted theme to the root element.
  private readonly theme = inject(ThemeService);
}
