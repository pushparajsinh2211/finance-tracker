INSERT INTO storage.buckets (id, name, public) 
VALUES ('receipts', 'receipts', true)
ON CONFLICT (id) DO NOTHING;

CREATE POLICY "Give public access to receipts"
ON storage.objects FOR SELECT 
USING (bucket_id = 'receipts');

CREATE POLICY "Allow authenticated uploads"
ON storage.objects FOR INSERT 
WITH CHECK (bucket_id = 'receipts' AND auth.role() = 'authenticated');

CREATE POLICY "Allow authenticated deletion"
ON storage.objects FOR DELETE 
USING (bucket_id = 'receipts' AND auth.role() = 'authenticated');
