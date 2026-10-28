import { Injectable } from '@angular/core';
import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { environment } from '../environments/environment';
import { AuthService } from './auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class StorageService {
  private readonly maxReceiptBytes = 10 * 1024 * 1024;
  private readonly allowedReceiptTypes = new Set([
    'image/jpeg',
    'image/png',
    'image/webp',
    'application/pdf'
  ]);

  constructor(private authService: AuthService) { }

  async uploadReceipt(file: File): Promise<string> {
    const { userId, supabase } = this.getStorageContext();

    if (!this.allowedReceiptTypes.has(file.type)) {
      throw new Error('Receipts must be JPEG, PNG, WebP, or PDF files.');
    }
    if (file.size > this.maxReceiptBytes) {
      throw new Error('Receipts must be 10 MB or smaller.');
    }

    const fileExt = file.name.split('.').pop()?.toLowerCase() || 'file';
    const fileName = Date.now() + '-' + Math.random().toString(36).slice(2) + '.' + fileExt;
    const filePath = userId + '/' + fileName;

    const { error } = await supabase.storage.from('receipts').upload(filePath, file, {
      contentType: file.type,
      upsert: false
    });
    if (error) throw error;

    return filePath;
  }

  async getReceiptUrl(filePath: string): Promise<string> {
    const { userId, supabase } = this.getStorageContext();
    this.assertOwnedReceiptPath(filePath, userId);

    const { data, error } = await supabase.storage
      .from('receipts')
      .createSignedUrl(filePath, 300);

    if (error) throw error;
    return data.signedUrl;
  }

  async deleteReceipt(filePath: string): Promise<void> {
    if (!filePath) return;

    const { userId, supabase } = this.getStorageContext();
    this.assertOwnedReceiptPath(filePath, userId);

    const { error } = await supabase.storage.from('receipts').remove([filePath]);
    if (error) throw error;
  }

  private getStorageContext(): { userId: string; supabase: SupabaseClient } {
    const token = this.authService.getAuthToken();
    if (!token) throw new Error('No auth token available.');

    return {
      userId: this.getUserId(token),
      supabase: this.createAuthenticatedClient(token)
    };
  }

  private assertOwnedReceiptPath(filePath: string, userId: string): void {
    if (!filePath.startsWith(userId + '/') || filePath.includes('..') || filePath.includes('\\')) {
      throw new Error('Receipt access is not allowed.');
    }
  }

  private getUserId(token: string): string {
    const encodedPayload = token.split('.')[1];
    if (!encodedPayload) throw new Error('Invalid authentication token.');

    const normalized = encodedPayload.replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
    const payload = JSON.parse(atob(padded));

    if (typeof payload.sub !== 'string' || !payload.sub) {
      throw new Error('User ID is missing from the authentication token.');
    }

    return payload.sub;
  }

  private createAuthenticatedClient(token: string): SupabaseClient {
    return createClient(environment.supabaseUrl, environment.supabaseKey, {
      auth: {
        persistSession: false,
        autoRefreshToken: false,
        detectSessionInUrl: false
      },
      global: {
        headers: {
          Authorization: 'Bearer ' + token
        }
      }
    });
  }
}
