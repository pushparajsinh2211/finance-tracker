import { Injectable } from '@angular/core';
import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { environment } from '../environments/environment';
import { AuthService } from './auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class StorageService {
  constructor(private authService: AuthService) { }

  async uploadReceipt(file: File): Promise<string> {
    const token = this.authService.getAuthToken();
    if (!token) throw new Error("No auth token available.");

    const supabase = this.createAuthenticatedClient(token);
    const fileExt = file.name.split('.').pop() || 'file';
    const fileName = `${Date.now()}-${Math.random().toString(36).slice(2)}.${fileExt}`;
    const filePath = `user-uploads/${fileName}`;

    const { error } = await supabase.storage.from('receipts').upload(filePath, file);
    if (error) throw error;

    const { data } = supabase.storage.from('receipts').getPublicUrl(filePath);
    return data.publicUrl;
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
          Authorization: `Bearer ${token}`
        }
      }
    });
  }
}
