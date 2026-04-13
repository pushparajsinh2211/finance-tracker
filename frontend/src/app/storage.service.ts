import { Injectable } from '@angular/core';
import { createClient, SupabaseClient } from '@supabase/supabase-js';
import { environment } from '../environments/environment';
import { AuthService } from './auth/auth.service';

@Injectable({
  providedIn: 'root'
})
export class StorageService {
  private supabase: SupabaseClient;

  constructor(private authService: AuthService) {
    this.supabase = createClient(environment.supabaseUrl, environment.supabaseKey);
  }

  async uploadReceipt(file: File): Promise<string> {
    const token = this.authService.getAuthToken();
    if (!token) throw new Error("No auth token available.");

    await this.supabase.auth.setSession({
      access_token: token,
      refresh_token: token
    });

    const fileExt = file.name.split('.').pop();
    const fileName = `${Date.now()}-${Math.random()}.${fileExt}`;
    const filePath = `user-uploads/${fileName}`;

    const { error } = await this.supabase.storage.from('receipts').upload(filePath, file);
    if (error) throw error;

    const { data } = this.supabase.storage.from('receipts').getPublicUrl(filePath);
    return data.publicUrl;
  }
}
