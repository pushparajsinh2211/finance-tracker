-- Keep financial receipts private and scoped to the authenticated owner.
UPDATE storage.buckets
SET public = false
WHERE id = 'receipts';

DROP POLICY IF EXISTS "Give public access to receipts" ON storage.objects;
DROP POLICY IF EXISTS "Allow authenticated uploads" ON storage.objects;
DROP POLICY IF EXISTS "Allow authenticated deletion" ON storage.objects;

CREATE POLICY "Users can view their own receipts"
ON storage.objects FOR SELECT
TO authenticated
USING (
    bucket_id = 'receipts'
    AND (
        (storage.foldername(name))[1] = (SELECT auth.uid()::text)
        OR owner_id = (SELECT auth.uid()::text)
    )
);

CREATE POLICY "Users can upload their own receipts"
ON storage.objects FOR INSERT
TO authenticated
WITH CHECK (
    bucket_id = 'receipts'
    AND (storage.foldername(name))[1] = (SELECT auth.uid()::text)
);

CREATE POLICY "Users can delete their own receipts"
ON storage.objects FOR DELETE
TO authenticated
USING (
    bucket_id = 'receipts'
    AND (
        (storage.foldername(name))[1] = (SELECT auth.uid()::text)
        OR owner_id = (SELECT auth.uid()::text)
    )
);
