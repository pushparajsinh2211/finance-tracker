BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap WITH SCHEMA extensions;
SET search_path = public, extensions;

SELECT plan(13);

INSERT INTO auth.users (id, email)
VALUES
    ('10000000-0000-0000-0000-000000000001', 'head@example.test'),
    ('10000000-0000-0000-0000-000000000002', 'member@example.test'),
    ('10000000-0000-0000-0000-000000000003', 'other-head@example.test');

SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '10000000-0000-0000-0000-000000000001',
    true
);

SELECT lives_ok(
    $$INSERT INTO public.families (
        id, name, head_user_id, invite_code, invite_active
    ) VALUES (
        '20000000-0000-0000-0000-000000000001',
        'Primary Family',
        '10000000-0000-0000-0000-000000000001',
        'JOINCODE',
        true
    )$$,
    'a user can create a family they head'
);

SELECT lives_ok(
    $$INSERT INTO public.family_members (
        id, family_id, user_id, display_name, relation, is_dependent
    ) VALUES (
        '30000000-0000-0000-0000-000000000001',
        '20000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000001',
        'Family Head',
        'Head',
        false
    )$$,
    'the family creator can create their own head membership'
);

RESET ROLE;
SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '10000000-0000-0000-0000-000000000002',
    true
);

SELECT throws_ok(
    $$INSERT INTO public.family_members (
        family_id, user_id, display_name, relation
    ) VALUES (
        '20000000-0000-0000-0000-000000000001',
        '10000000-0000-0000-0000-000000000002',
        'Bypass Attempt',
        'Member'
    )$$,
    '42501',
    'new row violates row-level security policy for table "family_members"',
    'a member cannot bypass the invite-code function'
);

SELECT is(
    public.join_family_by_code(' joincode ', ' Member User '),
    '20000000-0000-0000-0000-000000000001'::uuid,
    'a valid invite code joins the family'
);

SELECT is(
    (SELECT display_name
     FROM public.family_members
     WHERE user_id = auth.uid()),
    'Member User',
    'the join function trims the display name'
);

SELECT is(
    (public.get_my_family_details() ->> 'inviteCode'),
    '',
    'a non-head member does not receive the invite code'
);

SELECT is(
    (SELECT COUNT(*) FROM public.families),
    0::bigint,
    'a non-head member cannot query the family row directly'
);

UPDATE public.family_members
SET relation = 'Head'
WHERE user_id = auth.uid();

SELECT is(
    (SELECT relation
     FROM public.family_members
     WHERE user_id = auth.uid()),
    'Member',
    'a member cannot promote themselves'
);

RESET ROLE;
INSERT INTO public.families (
    id, name, head_user_id, invite_code, invite_active
) VALUES (
    '20000000-0000-0000-0000-000000000002',
    'Other Family',
    '10000000-0000-0000-0000-000000000003',
    'OTHERCOD',
    true
);
INSERT INTO public.family_members (
    id, family_id, user_id, display_name, relation, is_dependent
) VALUES (
    '30000000-0000-0000-0000-000000000003',
    '20000000-0000-0000-0000-000000000002',
    '10000000-0000-0000-0000-000000000003',
    'Other Head',
    'Head',
    false
);

SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '10000000-0000-0000-0000-000000000002',
    true
);

SELECT throws_ok(
    $$SELECT public.join_family_by_code('OTHERCOD', 'Member User')$$,
    'P0001',
    'User already belongs to a family',
    'a user cannot join a second family'
);

RESET ROLE;
SET LOCAL ROLE authenticated;
SELECT set_config(
    'request.jwt.claim.sub',
    '10000000-0000-0000-0000-000000000001',
    true
);

SELECT is(
    (public.get_my_family_details() ->> 'inviteCode'),
    'JOINCODE',
    'the family head receives the invite code'
);

SELECT lives_ok(
    $$UPDATE public.family_members
      SET is_dependent = false
      WHERE user_id = '10000000-0000-0000-0000-000000000002'$$,
    'the family head can change a member dependent status'
);

DELETE FROM public.family_members
WHERE user_id = '10000000-0000-0000-0000-000000000001';

SELECT ok(
    EXISTS (
        SELECT 1
        FROM public.family_members
        WHERE user_id = '10000000-0000-0000-0000-000000000001'
          AND lower(relation) = 'head'
    ),
    'the family head cannot delete their own head membership'
);

SELECT is(
    public.get_my_family_id(),
    '20000000-0000-0000-0000-000000000001'::uuid,
    'the family helper returns the current family'
);

SELECT * FROM finish();
ROLLBACK;
