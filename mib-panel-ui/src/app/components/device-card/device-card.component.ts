import { Component, Input } from '@angular/core';
import { DeviceIdentity } from '../../services/field-classifier.service';

@Component({
  selector: 'app-device-card',
  standalone: true,
  templateUrl: './device-card.component.html',
  styleUrl: './device-card.component.scss',
})
export class DeviceCardComponent {
  @Input({ required: true }) identity!: DeviceIdentity;
  @Input() community = '';
  @Input() snmpVersion = '';
  @Input() port = 161;
}
