BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap WITH SCHEMA extensions;
SET search_path = public, extensions;

SELECT plan(8);

INSERT INTO auth.users (id, email)
VALUES
    ('13000000-0000-0000-0000-000000000001', 'review-owner@example.test'),
    ('13000000-0000-0000-0000-000000000002', 'review-other@example.test');

INSERT INTO public.families (
    id, name, head_user_id, invite_code, invite_active
) VALUES (
    '23000000-0000-0000-0000-000000000001',
    'Review Family',
    '13000000-0000-0000-0000-000000000001',
    'REVIEW01',
    true
);

INSERT INTO public.family_members (
    id, family_id, user_id, display_name, relation, is_dependent
) VALUES (
    '33000000-0000-0000-0000-000000000001',
    '23000000-0000-0000-0000-000000000001',
    '13000000-0000-0000-0000-000000000001',
    'Review Owner',
    'Head',
    false
);

INSERT INTO public.categories (
    id, family_id, name, color, is_default, is_archived
) VALUES (
    '43000000-0000-0000-0000-000000000001',
    '23000000-0000-0000-0000-000000000001',
    'Transport',
    '#2563eb',
    false,
    false
);

SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '13000000-0000-0000-0000-000000000001',
    true
);

SELECT lives_ok(
    $test$SELECT public.import_financehub_statement(
        'bank',
        'csv',
        'Review Bank',
        'xxxx5678',
        repeat('d', 64),
        '[{
          "occurredAt": "2026-10-15T04:30:00Z",
          "amount": 100.00,
          "direction": "debit",
          "narration": "UPI bus ticket",
          "externalReference": "REVIEW-001",
          "paymentRail": "UPI",
          "transactionType": "purchase",
          "needsReview": true,
          "occurrenceIndex": 1
        }]'::jsonb
    )$test$,
    'a transaction is available for review'
);

SELECT is(
    (
        public.list_financehub_transactions() ->> 'totalCount'
    )::integer,
    1,
    'the owner can list the imported transaction'
);

SELECT lives_ok(
    $test$SELECT public.review_financehub_transaction(
        (
            SELECT id
            FROM public.financehub_transactions
            WHERE narration = 'UPI bus ticket'
        ),
        '43000000-0000-0000-0000-000000000001',
        false
    )$test$,
    'the owner can categorize and review the transaction'
);

SELECT is(
    (
        SELECT category_id
        FROM public.financehub_transactions
        WHERE narration = 'UPI bus ticket'
    ),
    '43000000-0000-0000-0000-000000000001'::uuid,
    'the selected family category is stored'
);

SELECT is(
    (SELECT count(*) FROM public.financehub_transaction_audit),
    1::bigint,
    'the review creates an immutable audit event'
);

UPDATE public.financehub_transactions
SET amount = 1
WHERE narration = 'UPI bus ticket';

SELECT is(
    (
        SELECT amount
        FROM public.financehub_transactions
        WHERE narration = 'UPI bus ticket'
    ),
    100.00::numeric,
    'direct client updates remain blocked'
);

DO $block$
DECLARE
    transaction_id_value UUID;
BEGIN
    SELECT id
    INTO transaction_id_value
    FROM public.financehub_transactions
    WHERE narration = 'UPI bus ticket';

    PERFORM set_config('test.financehub_transaction_id', transaction_id_value::text, true);
END
$block$;

RESET ROLE;
SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '13000000-0000-0000-0000-000000000002',
    true
);

SELECT is(
    (
        public.list_financehub_transactions() ->> 'totalCount'
    )::integer,
    0,
    'another user cannot list the transaction'
);

SELECT throws_ok(
    $test$SELECT public.review_financehub_transaction(
        current_setting('test.financehub_transaction_id')::uuid,
        NULL,
        false
    )$test$,
    '42501',
    NULL,
    'another user cannot review the transaction'
);

SELECT * FROM finish();
ROLLBACK;
