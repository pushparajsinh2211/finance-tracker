-- Optional, private, time-limited retention for original statement files.
-- Statement retention is disabled by default in the application.

INSERT INTO storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
VALUES (
    'financehub-statements',
    'financehub-statements',
    false,
    10485760,
    ARRAY['text/csv', 'application/csv', 'application/pdf']
)
ON CONFLICT (id) DO UPDATE SET
    public = false,
    file_size_limit = EXCLUDED.file_size_limit,
    allowed_mime_types = EXCLUDED.allowed_mime_types;

CREATE TABLE IF NOT EXISTS public.statement_import_files (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    import_run_id UUID NOT NULL REFERENCES public.import_runs(id) ON DELETE CASCADE,
    object_path TEXT NOT NULL UNIQUE,
    original_file_name TEXT NOT NULL,
    mime_type TEXT NOT NULL,
    byte_size BIGINT NOT NULL CHECK (byte_size > 0 AND byte_size <= 10485760),
    checksum TEXT NOT NULL CHECK (char_length(checksum) = 64),
    retention_days INTEGER NOT NULL CHECK (retention_days IN (30, 90)),
    delete_after TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at TIMESTAMPTZ,
    UNIQUE (import_run_id)
);

ALTER TABLE public.statement_import_files ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users read own retained statement metadata"
    ON public.statement_import_files
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users read own retained statement objects"
    ON storage.objects
    FOR SELECT TO authenticated
    USING (
        bucket_id = 'financehub-statements'
        AND (storage.foldername(name))[1] = auth.uid()::text
    );

CREATE POLICY "Users upload own retained statement objects"
    ON storage.objects
    FOR INSERT TO authenticated
    WITH CHECK (
        bucket_id = 'financehub-statements'
        AND (storage.foldername(name))[1] = auth.uid()::text
    );

CREATE POLICY "Users update own retained statement objects"
    ON storage.objects
    FOR UPDATE TO authenticated
    USING (
        bucket_id = 'financehub-statements'
        AND (storage.foldername(name))[1] = auth.uid()::text
    )
    WITH CHECK (
        bucket_id = 'financehub-statements'
        AND (storage.foldername(name))[1] = auth.uid()::text
    );

CREATE POLICY "Users delete own retained statement objects"
    ON storage.objects
    FOR DELETE TO authenticated
    USING (
        bucket_id = 'financehub-statements'
        AND (storage.foldername(name))[1] = auth.uid()::text
    );

CREATE OR REPLACE FUNCTION public.record_retained_statement(
    p_import_run_id UUID,
    p_object_path TEXT,
    p_original_file_name TEXT,
    p_mime_type TEXT,
    p_byte_size BIGINT,
    p_checksum TEXT,
    p_retention_days INTEGER
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    current_user_id UUID := auth.uid();
    retained public.statement_import_files%ROWTYPE;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication is required' USING ERRCODE = '42501';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM public.import_runs
        WHERE id = p_import_run_id AND user_id = current_user_id
    ) THEN
        RAISE EXCEPTION 'Import run not found' USING ERRCODE = '42501';
    END IF;

    IF p_object_path IS NULL
       OR p_object_path NOT LIKE current_user_id::text || '/%'
       OR p_retention_days NOT IN (30, 90)
       OR p_mime_type NOT IN ('text/csv', 'application/csv', 'application/pdf')
       OR p_byte_size <= 0
       OR p_byte_size > 10485760
       OR char_length(p_checksum) <> 64 THEN
        RAISE EXCEPTION 'Invalid retained statement metadata' USING ERRCODE = '22023';
    END IF;

    INSERT INTO public.statement_import_files (
        user_id,
        import_run_id,
        object_path,
        original_file_name,
        mime_type,
        byte_size,
        checksum,
        retention_days,
        delete_after
    )
    VALUES (
        current_user_id,
        p_import_run_id,
        p_object_path,
        left(p_original_file_name, 180),
        p_mime_type,
        p_byte_size,
        p_checksum,
        p_retention_days,
        now() + make_interval(days => p_retention_days)
    )
    ON CONFLICT (import_run_id) DO UPDATE SET
        object_path = EXCLUDED.object_path,
        original_file_name = EXCLUDED.original_file_name,
        mime_type = EXCLUDED.mime_type,
        byte_size = EXCLUDED.byte_size,
        checksum = EXCLUDED.checksum,
        retention_days = EXCLUDED.retention_days,
        delete_after = EXCLUDED.delete_after,
        deleted_at = NULL
    RETURNING * INTO retained;

    RETURN jsonb_build_object(
        'retained', true,
        'objectPath', retained.object_path,
        'deleteAfter', retained.delete_after,
        'retentionDays', retained.retention_days
    );
END
$$;

CREATE OR REPLACE FUNCTION public.mark_retained_statement_deleted(p_import_run_id UUID)
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    UPDATE public.statement_import_files
    SET deleted_at = now()
    WHERE import_run_id = p_import_run_id
      AND user_id = auth.uid()
      AND deleted_at IS NULL;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Retained statement not found' USING ERRCODE = 'P0002';
    END IF;
END
$$;

REVOKE ALL ON FUNCTION public.record_retained_statement(UUID, TEXT, TEXT, TEXT, BIGINT, TEXT, INTEGER) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.record_retained_statement(UUID, TEXT, TEXT, TEXT, BIGINT, TEXT, INTEGER) TO authenticated;
REVOKE ALL ON FUNCTION public.mark_retained_statement_deleted(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.mark_retained_statement_deleted(UUID) TO authenticated;

COMMENT ON TABLE public.statement_import_files IS
    'Metadata for optional private original-statement retention. Files expire after 30 or 90 days.';
