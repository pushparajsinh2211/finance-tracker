BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap WITH SCHEMA extensions;
SET search_path = public, extensions;
SELECT plan(7);

INSERT INTO auth.users (id, email)
VALUES
    ('13000000-0000-0000-0000-000000000001', 'retention-owner@example.test'),
    ('13000000-0000-0000-0000-000000000002', 'retention-other@example.test');

INSERT INTO public.import_runs (
    id, user_id, source_kind, idempotency_key, status, started_at, completed_at
) VALUES (
    '23000000-0000-0000-0000-000000000001',
    '13000000-0000-0000-0000-000000000001',
    'csv',
    repeat('d', 64),
    'completed',
    now(),
    now()
);

SET LOCAL ROLE authenticated;
SELECT set_config('request.jwt.claim.sub', '13000000-0000-0000-0000-000000000001', true);

SELECT lives_ok(
    $$SELECT public.record_retained_statement(
        '23000000-0000-0000-0000-000000000001',
        '13000000-0000-0000-0000-000000000001/23000000-0000-0000-0000-000000000001/statement.csv',
        'redacted.csv',
        'text/csv',
        128,
        repeat('e', 64),
        30
    )$$,
    'an import owner can record private statement retention'
);

SELECT is(
    (SELECT count(*) FROM public.statement_import_files),
    1::bigint,
    'the owner can read retained statement metadata'
);

SELECT ok(
    (SELECT delete_after BETWEEN now() + interval '29 days' AND now() + interval '31 days'
     FROM public.statement_import_files),
    'the selected retention period controls expiry'
);

SELECT throws_ok(
    $$SELECT public.record_retained_statement(
        '23000000-0000-0000-0000-000000000001',
        'another-user/statement.csv',
        'bad.csv',
        'text/csv',
        10,
        repeat('f', 64),
        30
    )$$,
    '22023',
    'Invalid retained statement metadata',
    'object paths must stay inside the authenticated user prefix'
);

SELECT throws_ok(
    $$INSERT INTO public.statement_import_files (
        user_id, import_run_id, object_path, original_file_name, mime_type,
        byte_size, checksum, retention_days, delete_after
    ) VALUES (
        auth.uid(),
        '23000000-0000-0000-0000-000000000001',
        auth.uid()::text || '/direct.csv',
        'direct.csv',
        'text/csv',
        10,
        repeat('1', 64),
        30,
        now() + interval '30 days'
    )$$,
    '42501',
    NULL,
    'clients cannot bypass the guarded retention RPC'
);

RESET ROLE;
SET LOCAL ROLE authenticated;
SELECT set_config('request.jwt.claim.sub', '13000000-0000-0000-0000-000000000002', true);

SELECT is(
    (SELECT count(*) FROM public.statement_import_files),
    0::bigint,
    'another user cannot read retained statement metadata'
);

SELECT throws_ok(
    $$SELECT public.mark_retained_statement_deleted(
        '23000000-0000-0000-0000-000000000001'
    )$$,
    'P0002',
    'Retained statement not found',
    'another user cannot mark the retained statement deleted'
);

SELECT * FROM finish();
ROLLBACK;
