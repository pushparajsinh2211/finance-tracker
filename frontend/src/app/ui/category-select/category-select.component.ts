import { CommonModule } from '@angular/common';
import {
  Component,
  ElementRef,
  forwardRef,
  HostListener,
  Input
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

export interface CategorySelectOption {
  id: string;
  name: string;
  color?: string;
}

@Component({
  selector: 'app-category-select',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './category-select.component.html',
  styleUrls: ['./category-select.component.css'],
  providers: [{
    provide: NG_VALUE_ACCESSOR,
    useExisting: forwardRef(() => CategorySelectComponent),
    multi: true
  }]
})
export class CategorySelectComponent implements ControlValueAccessor {
  @Input() inputId = 'financehub-category-select';
  @Input() placeholder = 'Select a category';
  @Input() emptyLabel = 'No category';
  @Input() allowClear = false;
  @Input() options: CategorySelectOption[] = [];

  value = '';
  isOpen = false;
  isDisabled = false;

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  constructor(private readonly elementRef: ElementRef<HTMLElement>) {}

  get selectedOption(): CategorySelectOption | undefined {
    return this.options.find(option => option.id === this.value);
  }

  writeValue(value: string | null): void {
    this.value = value || '';
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
    if (!this.isOpen) this.onTouched();
  }

  select(option: CategorySelectOption): void {
    this.commit(option.id);
  }

  clearSelection(): void {
    this.commit('');
  }

  close(): void {
    if (!this.isOpen) return;
    this.isOpen = false;
    this.onTouched();
  }

  trackOption(_index: number, option: CategorySelectOption): string {
    return option.id;
  }

  @HostListener('document:click', ['$event'])
  handleDocumentClick(event: MouseEvent): void {
    if (this.isOpen && !this.elementRef.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  @HostListener('keydown.escape')
  handleEscape(): void {
    this.close();
  }

  private commit(value: string): void {
    this.value = value;
    this.onChange(value);
    this.onTouched();
    this.isOpen = false;
  }
}
