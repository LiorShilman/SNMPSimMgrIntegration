export interface MibPanelSchema {
  deviceName: string;
  deviceIp: string;
  devicePort: number;
  community: string;
  snmpVersion: string;
  exportedAt: string;
  totalFields: number;
  modules: MibModuleSchema[];
}

export interface MibModuleSchema {
  moduleName: string;
  scalarCount: number;
  tableCount: number;
  scalars: MibFieldSchema[];
  tables: MibTableSchema[];
}

export interface MibFieldSchema {
  oid: string;
  name: string;
  description?: string;
  access: string;
  isWritable: boolean;
  inputType: string;   // text | number | enum | toggle | ip | oid | gauge | counter | timeticks | bits
  baseType: string;
  units?: string;
  displayHint?: string;
  minValue?: number;
  maxValue?: number;
  minLength?: number;
  maxLength?: number;
  defaultValue?: string;
  options?: EnumOption[];
  currentValue?: string;
  currentValueType?: string;
  status?: string;
  tableIndex?: string;
}

export interface MibTableSchema {
  name: string;
  oid: string;
  description?: string;
  labelColumn?: string;
  rowCount: number;
  columnCount: number;
  columns: MibFieldSchema[];
  rows: MibTableRow[];
}

export interface MibTableRow {
  index: string;
  label?: string;
  values: Record<string, MibCellValue>;
}

export interface MibCellValue {
  value: string;
  type?: string;
  enumLabel?: string;
}

export interface EnumOption {
  label: string;
  value: number;
}

export interface SetFeedback {
  id: number;
  oid: string;
  name: string;
  value: string;
  valueType: string;
  timestamp: Date;
  status: 'pending' | 'success' | 'error';
  message?: string;
}
