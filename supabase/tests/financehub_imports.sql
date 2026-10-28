BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap WITH SCHEMA extensions;
SET search_path = public, extensions;

SELECT plan(9);

INSERT INTO auth.users (id, email)
VALUES
    ('12000000-0000-0000-0000-000000000001', 'import-owner@example.test'),
    ('12000000-0000-0000-0000-000000000002', 'other-import-owner@example.test');

INSERT INTO public.families (
    id, name, head_user_id, invite_code, invite_active
) VALUES (
    '22000000-0000-0000-0000-000000000001',
    'Import Family',
    '12000000-0000-0000-0000-000000000001',
    'IMPORT01',
    true
);

INSERT INTO public.family_members (
    id, family_id, user_id, display_name, relation, is_dependent
) VALUES (
    '32000000-0000-0000-0000-000000000001',
    '22000000-0000-0000-0000-000000000001',
    '12000000-0000-0000-0000-000000000001',
    'Import Owner',
    'Head',
    false
);

SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '12000000-0000-0000-0000-000000000001',
    true
);

SELECT lives_ok(
    $$SELECT public.import_financehub_statement(
        'bank',
        'csv',
        'Primary Bank',
        'xxxx1234',
        repeat('a', 64),
        '[
          {
            "occurredAt": "2026-10-01T04:30:00Z",
            "amount": 250.50,
            "direction": "debit",
            "narration": "UPI/DR/123456789012/Grocery",
            "externalReference": "123456789012",
            "paymentRail": "UPI",
            "transactionType": "purchase",
            "needsReview": false
          },
          {
            "occurredAt": "2026-10-02T04:30:00Z",
            "amount": 50000,
            "direction": "credit",
            "narration": "Salary",
            "externalReference": "SALARY-2026-10",
            "paymentRail": "NEFT",
            "transactionType": "income",
            "needsReview": false
          }
        ]'::jsonb
    )$$,
    'a valid statement imports atomically'
);

SELECT is(
    (SELECT COUNT(*) FROM public.financial_accounts),
    1::bigint,
    'one statement account is created'
);

SELECT is(
    (SELECT COUNT(*) FROM public.financehub_transactions),
    2::bigint,
    'canonical transactions are created'
);

SELECT is(
    (SELECT payment_rail
     FROM public.financehub_transactions
     WHERE narration LIKE 'UPI/%'),
    'UPI',
    'UPI detection is retained in the canonical row'
);

SELECT is(
    (
        public.import_financehub_statement(
            'bank',
            'csv',
            'Primary Bank',
            'xxxx1234',
            repeat('a', 64),
            '[{
              "occurredAt": "2026-10-01T04:30:00Z",
              "amount": 250.50,
              "direction": "debit",
              "narration": "UPI/DR/123456789012/Grocery",
              "externalReference": "123456789012",
              "paymentRail": "UPI",
              "transactionType": "purchase",
              "needsReview": false
            }]'::jsonb
        ) ->> 'alreadyImported'
    )::boolean,
    true,
    'the same idempotency key returns the prior import'
);

SELECT is(
    (SELECT COUNT(*) FROM public.financehub_transactions),
    2::bigint,
    'an idempotent retry does not duplicate transactions'
);

SELECT throws_ok(
    $$INSERT INTO public.raw_transactions (
        user_id, import_run_id, payload, payload_checksum
    )
    SELECT
        auth.uid(),
        id,
        '{}'::jsonb,
        repeat('b', 64)
    FROM public.import_runs
    LIMIT 1$$,
    '42501',
    NULL,
    'clients cannot mutate immutable raw ingestion records'
);

UPDATE public.financehub_transactions
SET amount = 1;

SELECT is(
    (
        SELECT amount
        FROM public.financehub_transactions
        WHERE narration = 'Salary'
    ),
    50000::numeric,
    'clients cannot directly rewrite canonical imported transactions'
);

RESET ROLE;
SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '12000000-0000-0000-0000-000000000002',
    true
);

SELECT is(
    (SELECT COUNT(*) FROM public.financehub_transactions),
    0::bigint,
    'another user cannot read imported transactions'
);

SELECT * FROM finish();
ROLLBACK;
