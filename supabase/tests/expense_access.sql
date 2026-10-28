BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap WITH SCHEMA extensions;
SET search_path = public, extensions;

SELECT plan(11);

INSERT INTO auth.users (id, email)
VALUES
    ('11000000-0000-0000-0000-000000000001', 'expense-owner@example.test'),
    ('11000000-0000-0000-0000-000000000002', 'other-expense-owner@example.test');

INSERT INTO public.families (
    id, name, head_user_id, invite_code, invite_active
) VALUES
    (
        '21000000-0000-0000-0000-000000000001',
        'Expense Family',
        '11000000-0000-0000-0000-000000000001',
        'EXPFAM01',
        true
    ),
    (
        '21000000-0000-0000-0000-000000000002',
        'Other Expense Family',
        '11000000-0000-0000-0000-000000000002',
        'EXPFAM02',
        true
    );

INSERT INTO public.family_members (
    id, family_id, user_id, display_name, relation, is_dependent
) VALUES
    (
        '31000000-0000-0000-0000-000000000001',
        '21000000-0000-0000-0000-000000000001',
        '11000000-0000-0000-0000-000000000001',
        'Expense Owner',
        'Head',
        false
    ),
    (
        '31000000-0000-0000-0000-000000000002',
        '21000000-0000-0000-0000-000000000002',
        '11000000-0000-0000-0000-000000000002',
        'Other Expense Owner',
        'Head',
        false
    );

INSERT INTO public.categories (
    id, family_id, name, color, is_default, is_archived
) VALUES
    (
        '41000000-0000-0000-0000-000000000001',
        '21000000-0000-0000-0000-000000000001',
        'Groceries',
        '#22C55E',
        true,
        false
    ),
    (
        '41000000-0000-0000-0000-000000000002',
        '21000000-0000-0000-0000-000000000002',
        'Other Groceries',
        '#EF4444',
        true,
        false
    ),
    (
        '41000000-0000-0000-0000-000000000003',
        '21000000-0000-0000-0000-000000000001',
        'Archived Category',
        '#64748B',
        false,
        true
    );

SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '11000000-0000-0000-0000-000000000001',
    true
);

SELECT lives_ok(
    $$INSERT INTO public.expenses (
        id, user_id, family_id, category_id, amount, date, note
    ) VALUES (
        '51000000-0000-0000-0000-000000000001',
        '11000000-0000-0000-0000-000000000001',
        '21000000-0000-0000-0000-000000000001',
        '41000000-0000-0000-0000-000000000001',
        1250.50,
        CURRENT_DATE,
        'Weekly groceries'
    )$$,
    'an authenticated user can create an expense in their current family'
);

SELECT throws_ok(
    $$INSERT INTO public.expenses (
        user_id, family_id, category_id, amount, date
    ) VALUES (
        '11000000-0000-0000-0000-000000000001',
        '21000000-0000-0000-0000-000000000002',
        '41000000-0000-0000-0000-000000000002',
        10,
        CURRENT_DATE
    )$$,
    '23514',
    'Expense category must be active and belong to the expense family',
    'a user cannot insert an expense for another family'
);

SELECT throws_ok(
    $$INSERT INTO public.expenses (
        user_id, family_id, category_id, amount, date
    ) VALUES (
        '11000000-0000-0000-0000-000000000001',
        '21000000-0000-0000-0000-000000000001',
        '41000000-0000-0000-0000-000000000002',
        10,
        CURRENT_DATE
    )$$,
    '23514',
    'Expense category must be active and belong to the expense family',
    'a user cannot assign another family category'
);

SELECT is(
    (SELECT COUNT(*) FROM public.expenses),
    1::bigint,
    'the owner sees their own current-family expense'
);

SELECT lives_ok(
    $$UPDATE public.expenses
      SET amount = 1300
      WHERE id = '51000000-0000-0000-0000-000000000001'$$,
    'the owner can update their expense'
);

SELECT is(
    (SELECT amount FROM public.expenses
     WHERE id = '51000000-0000-0000-0000-000000000001'),
    1300::numeric,
    'the expense update is persisted'
);

SELECT throws_ok(
    $$UPDATE public.expenses
      SET user_id = '11000000-0000-0000-0000-000000000002'
      WHERE id = '51000000-0000-0000-0000-000000000001'$$,
    '42501',
    'new row violates row-level security policy for table "expenses"',
    'an owner cannot transfer an expense to another user'
);

SELECT throws_ok(
    $$UPDATE public.expenses
      SET category_id = '41000000-0000-0000-0000-000000000003'
      WHERE id = '51000000-0000-0000-0000-000000000001'$$,
    '23514',
    NULL,
    'an archived category cannot be newly assigned'
);

SELECT throws_ok(
    $$UPDATE public.expenses
      SET amount = -1
      WHERE id = '51000000-0000-0000-0000-000000000001'$$,
    '23514',
    NULL,
    'negative expense amounts are rejected'
);

RESET ROLE;
UPDATE public.categories
SET is_archived = true
WHERE id = '41000000-0000-0000-0000-000000000001';

SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '11000000-0000-0000-0000-000000000001',
    true
);

SELECT lives_ok(
    $$UPDATE public.expenses
      SET deleted_at = now()
      WHERE id = '51000000-0000-0000-0000-000000000001'$$,
    'the owner can soft-delete their expense'
);

RESET ROLE;
SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '11000000-0000-0000-0000-000000000002',
    true
);

SELECT is(
    (SELECT COUNT(*) FROM public.expenses
     WHERE id = '51000000-0000-0000-0000-000000000001'),
    0::bigint,
    'another family cannot read the expense'
);

SELECT * FROM finish();
ROLLBACK;
