import { Component, Input } from '@angular/core';
import { SystemInfoItem } from '../../services/field-classifier.service';

@Component({
  selector: 'app-system-info',
  standalone: true,
  templateUrl: './system-info.component.html',
  styleUrl: './system-info.component.scss',
})
export class SystemInfoComponent {
  @Input({ required: true }) items: SystemInfoItem[] = [];

  get identityItems(): SystemInfoItem[] {
    return this.items.filter(i => i.category === 'identity');
  }

  get systemItems(): SystemInfoItem[] {
    return this.items.filter(i => i.category === 'system');
  }

  get networkItems(): SystemInfoItem[] {
    return this.items.filter(i => i.category === 'network');
  }
}
