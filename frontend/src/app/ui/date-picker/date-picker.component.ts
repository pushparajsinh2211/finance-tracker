import { CommonModule } from '@angular/common';
import { Component, ElementRef, forwardRef, HostListener, Input } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

interface CalendarDay {
  value: string;
  dayNumber: number;
  outsideMonth: boolean;
  today: boolean;
  selected: boolean;
  disabled: boolean;
  ariaLabel: string;
}

@Component({
  selector: 'app-date-picker',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './date-picker.component.html',
  styleUrls: ['./date-picker.component.css'],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => DatePickerComponent),
    multi: true
  }]
})
export class DatePickerComponent implements ControlValueAccessor {
  @Input() inputId = 'financehub-date-picker';
  @Input() placeholder = 'Select a date';
  @Input() dialogLabel = 'Choose date';
  @Input() min = '';
  @Input() max = '';

  readonly weekdayLabels = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  value = '';
  isOpen = false;
  isDisabled = false;
  viewDate = this.startOfMonth(new Date());

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  constructor(private readonly elementRef: ElementRef<HTMLElement>) {}

  get displayValue(): string {
    const date = this.parseValue(this.value);
    return date
      ? new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', year: 'numeric' }).format(date)
      : '';
  }

  get monthLabel(): string {
    return new Intl.DateTimeFormat('en-IN', { month: 'long', year: 'numeric' }).format(this.viewDate);
  }

  get calendarDays(): CalendarDay[] {
    const year = this.viewDate.getFullYear();
    const month = this.viewDate.getMonth();
    const firstDay = new Date(year, month, 1);
    const mondayOffset = (firstDay.getDay() + 6) % 7;
    const gridStart = new Date(year, month, 1 - mondayOffset);
    const todayValue = this.toValue(new Date());

    return Array.from({ length: 42 }, (_, index) => {
      const date = new Date(gridStart);
      date.setDate(gridStart.getDate() + index);
      const value = this.toValue(date);
      return {
        value,
        dayNumber: date.getDate(),
        outsideMonth: date.getMonth() !== month,
        today: value === todayValue,
        selected: value === this.value,
        disabled: this.isOutsideRange(value),
        ariaLabel: new Intl.DateTimeFormat('en-IN', {
          weekday: 'long', day: 'numeric', month: 'long', year: 'numeric'
        }).format(date)
      };
    });
  }

  writeValue(value: string | null): void {
    this.value = value || '';
    const parsed = this.parseValue(this.value);
    if (parsed) this.viewDate = this.startOfMonth(parsed);
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.isDisabled = disabled;
    if (disabled) this.isOpen = false;
  }

  toggle(): void {
    if (this.isDisabled) return;
    this.isOpen = !this.isOpen;
    if (this.isOpen) {
      this.viewDate = this.startOfMonth(this.parseValue(this.value) || new Date());
    } else {
      this.onTouched();
    }
  }

  close(): void {
    if (!this.isOpen) return;
    this.isOpen = false;
    this.onTouched();
  }

  moveMonth(offset: number): void {
    this.viewDate = new Date(this.viewDate.getFullYear(), this.viewDate.getMonth() + offset, 1);
  }

  moveYear(offset: number): void {
    this.viewDate = new Date(this.viewDate.getFullYear() + offset, this.viewDate.getMonth(), 1);
  }

  selectDay(day: CalendarDay): void {
    if (!day.disabled) this.commit(day.value);
  }

  selectToday(): void {
    const value = this.toValue(new Date());
    if (!this.isOutsideRange(value)) this.commit(value);
  }

  isTodayDisabled(): boolean {
    return this.isOutsideRange(this.toValue(new Date()));
  }

  trackDay(_index: number, day: CalendarDay): string {
    return day.value;
  }

  @HostListener('document:click', ['$event'])
  handleDocumentClick(event: MouseEvent): void {
    if (this.isOpen && !this.elementRef.nativeElement.contains(event.target as Node)) this.close();
  }

  @HostListener('keydown.escape')
  handleEscape(): void {
    this.close();
  }

  private commit(value: string): void {
    this.value = value;
    const selected = this.parseValue(value);
    if (selected) this.viewDate = this.startOfMonth(selected);
    this.onChange(value);
    this.onTouched();
    this.isOpen = false;
  }

  private isOutsideRange(value: string): boolean {
    return (!!this.min && value < this.min) || (!!this.max && value > this.max);
  }

  private parseValue(value: string): Date | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
    if (!match) return null;
    const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
    return Number.isNaN(date.getTime()) ? null : date;
  }

  private toValue(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private startOfMonth(date: Date): Date {
    return new Date(date.getFullYear(), date.getMonth(), 1);
  }
}
