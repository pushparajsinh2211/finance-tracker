BEGIN;

CREATE EXTENSION IF NOT EXISTS pgtap WITH SCHEMA extensions;
SET search_path = public, extensions;

SELECT plan(13);

INSERT INTO auth.users (id, email)
VALUES
  ('11000000-0000-0000-0000-000000000001', 'privacy-head@example.test'),
  ('11000000-0000-0000-0000-000000000002', 'privacy-member@example.test');

INSERT INTO public.families (id, name, head_user_id, invite_code, invite_active)
VALUES ('21000000-0000-0000-0000-000000000001', 'Privacy Family',
  '11000000-0000-0000-0000-000000000001', 'PRIVACY1', true);

INSERT INTO public.family_members (id, family_id, user_id, display_name, relation, is_dependent)
VALUES
  ('31000000-0000-0000-0000-000000000001', '21000000-0000-0000-0000-000000000001',
   '11000000-0000-0000-0000-000000000001', 'Head', 'Head', false),
  ('31000000-0000-0000-0000-000000000002', '21000000-0000-0000-0000-000000000001',
   '11000000-0000-0000-0000-000000000002', 'Member', 'Member', false);

INSERT INTO public.categories (id, family_id, name, color, is_default)
VALUES ('41000000-0000-0000-0000-000000000001', '21000000-0000-0000-0000-000000000001',
  'Privacy Test', '#000000', false);

INSERT INTO public.expenses (id, user_id, family_id, category_id, amount, note)
VALUES
  ('51000000-0000-0000-0000-000000000001', '11000000-0000-0000-0000-000000000001',
   '21000000-0000-0000-0000-000000000001', '41000000-0000-0000-0000-000000000001',
   10, 'head-private'),
  ('51000000-0000-0000-0000-000000000002', '11000000-0000-0000-0000-000000000002',
   '21000000-0000-0000-0000-000000000001', '41000000-0000-0000-0000-000000000001',
   20, 'member-private');

SELECT ok(
  has_function_privilege('authenticated', 'public.export_my_data()', 'EXECUTE'),
  'authenticated users can execute their export'
);
SELECT ok(
  NOT has_function_privilege('anon', 'public.export_my_data()', 'EXECUTE'),
  'anonymous users cannot execute exports'
);
SELECT throws_ok(
  $$SELECT public.export_my_data()$$,
  '42501', 'Authentication required', 'an export requires a JWT user'
);

SET LOCAL ROLE authenticated;
SELECT set_config('request.jwt.claim.sub', '11000000-0000-0000-0000-000000000001', true);

SELECT is(public.get_account_deletion_status() ->> 'canDelete', 'false',
  'a head cannot delete while another member remains');
SELECT is((public.get_account_deletion_status() ->> 'familyMemberCount')::integer, 2,
  'preflight reports the member count');
SELECT is(public.export_my_data() #>> '{account,email}', 'privacy-head@example.test',
  'export identifies the authenticated user');
SELECT is(jsonb_array_length(public.export_my_data() -> 'expenses'), 1,
  'export includes only current-user expenses');
SELECT is(public.export_my_data() #>> '{expenses,0,note}', 'head-private',
  'head export does not contain member-owned expense');

RESET ROLE;
SET LOCAL ROLE authenticated;
SELECT set_config('request.jwt.claim.sub', '11000000-0000-0000-0000-000000000002', true);

SELECT is(public.get_account_deletion_status() ->> 'canDelete', 'true',
  'a non-head member may delete their account');
SELECT is(public.export_my_data() #>> '{expenses,0,note}', 'member-private',
  'member export remains scoped to that member');

RESET ROLE;
DELETE FROM public.family_members
WHERE user_id = '11000000-0000-0000-0000-000000000002';

SET LOCAL ROLE authenticated;
SELECT set_config('request.jwt.claim.sub', '11000000-0000-0000-0000-000000000001', true);

SELECT is(public.get_account_deletion_status() ->> 'canDelete', 'false',
  'former member family-linked data still blocks head deletion');
SELECT is(public.begin_account_deletion() ->> 'canDelete', 'false',
  'atomic deletion preparation rejects surviving data owned by another user');
SELECT is(
  (SELECT invite_active FROM public.families
   WHERE id = '21000000-0000-0000-0000-000000000001'),
  true,
  'a rejected deletion preparation leaves invitations unchanged'
);

SELECT * FROM finish();
ROLLBACK;
