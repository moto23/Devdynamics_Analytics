import { Component } from '@angular/core';
import { RouterModule } from '@angular/router'; // ✅ ADD THIS

@Component({
  selector: 'app-root',
  standalone: true, // ✅ VERY IMPORTANT
  imports: [RouterModule], // ✅ THIS FIXES ROUTING
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  isDarkMode = false;

  toggleTheme() {
    this.isDarkMode = !this.isDarkMode;
    document.body.classList.toggle('dark-mode');
  }
}