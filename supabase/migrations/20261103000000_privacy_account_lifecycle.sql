-- User privacy export and safe account-deletion preflight.
-- Authenticated callers can only inspect their own data.

CREATE TABLE public.account_deletion_reservations (
    user_id UUID PRIMARY KEY REFERENCES auth.users(id) ON DELETE CASCADE,
    family_id UUID NOT NULL REFERENCES public.families(id) ON DELETE CASCADE,
    invite_was_active BOOLEAN NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

REVOKE ALL ON public.account_deletion_reservations FROM PUBLIC, anon, authenticated;

CREATE OR REPLACE FUNCTION public.family_has_other_user_data(
    p_family_id UUID,
    p_user_id UUID
)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT
        EXISTS (
            SELECT 1 FROM public.family_members
            WHERE family_id = p_family_id AND user_id <> p_user_id
        )
        OR EXISTS (
            SELECT 1 FROM public.expenses
            WHERE family_id = p_family_id AND user_id <> p_user_id
        )
        OR EXISTS (
            SELECT 1 FROM public.financial_accounts
            WHERE family_id = p_family_id AND user_id <> p_user_id
        )
        OR EXISTS (
            SELECT 1 FROM public.financehub_transactions
            WHERE family_id = p_family_id AND user_id <> p_user_id
        )
        OR EXISTS (
            SELECT 1
            FROM public.budgets b
            JOIN public.categories c ON c.id = b.category_id
            WHERE c.family_id = p_family_id AND b.user_id <> p_user_id
        )
        OR EXISTS (
            SELECT 1
            FROM public.categorization_rules r
            JOIN public.categories c ON c.id = r.category_id
            WHERE c.family_id = p_family_id AND r.user_id <> p_user_id
        );
$$;

REVOKE ALL ON FUNCTION public.family_has_other_user_data(UUID, UUID) FROM PUBLIC, anon, authenticated;

CREATE OR REPLACE FUNCTION public.get_account_deletion_status()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, auth
AS $$
DECLARE
    current_user_id UUID := auth.uid();
    membership_row public.family_members%ROWTYPE;
    member_count INTEGER := 0;
    is_head BOOLEAN := false;
    has_other_data BOOLEAN := false;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication required' USING ERRCODE = '42501';
    END IF;

    SELECT *
    INTO membership_row
    FROM public.family_members
    WHERE user_id = current_user_id
    LIMIT 1;

    IF membership_row.id IS NOT NULL THEN
        is_head := lower(coalesce(membership_row.relation, '')) = 'head';

        SELECT count(*)
        INTO member_count
        FROM public.family_members
        WHERE family_id = membership_row.family_id;

        IF is_head THEN
            has_other_data := public.family_has_other_user_data(
                membership_row.family_id,
                current_user_id
            );
        END IF;
    END IF;

    RETURN jsonb_build_object(
        'canDelete', NOT (is_head AND has_other_data),
        'isFamilyHead', is_head,
        'familyId', membership_row.family_id,
        'familyMemberCount', member_count,
        'blocker', CASE
            WHEN is_head AND has_other_data
                THEN 'Transfer family ownership and preserve other users'' family-linked data before deleting this account.'
            ELSE NULL
        END
    );
END;
$$;

CREATE OR REPLACE FUNCTION public.begin_account_deletion()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, auth
AS $$
DECLARE
    current_user_id UUID := auth.uid();
    membership_row public.family_members%ROWTYPE;
    family_invite_active BOOLEAN;
    has_other_data BOOLEAN := false;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication required' USING ERRCODE = '42501';
    END IF;

    SELECT *
    INTO membership_row
    FROM public.family_members
    WHERE user_id = current_user_id
    LIMIT 1;

    IF membership_row.id IS NULL
       OR lower(coalesce(membership_row.relation, '')) <> 'head' THEN
        RETURN jsonb_build_object('canDelete', true, 'reservationCreated', false, 'blocker', NULL);
    END IF;

    -- join_family_by_code() locks this same row FOR UPDATE. This serializes
    -- deletion preparation against a concurrent invite-code join.
    SELECT invite_active
    INTO family_invite_active
    FROM public.families
    WHERE id = membership_row.family_id
      AND head_user_id = current_user_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Family ownership could not be verified';
    END IF;

    has_other_data := public.family_has_other_user_data(
        membership_row.family_id,
        current_user_id
    );

    IF has_other_data THEN
        RETURN jsonb_build_object(
            'canDelete', false,
            'reservationCreated', false,
            'blocker', 'Transfer family ownership and preserve other users'' family-linked data before deleting this account.'
        );
    END IF;

    INSERT INTO public.account_deletion_reservations (
        user_id,
        family_id,
        invite_was_active
    )
    VALUES (
        current_user_id,
        membership_row.family_id,
        family_invite_active
    )
    ON CONFLICT (user_id) DO NOTHING;

    UPDATE public.families
    SET invite_active = false
    WHERE id = membership_row.family_id;

    RETURN jsonb_build_object('canDelete', true, 'reservationCreated', true, 'blocker', NULL);
END;
$$;

CREATE OR REPLACE FUNCTION public.cancel_account_deletion()
RETURNS VOID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, auth
AS $$
DECLARE
    current_user_id UUID := auth.uid();
    reservation_row public.account_deletion_reservations%ROWTYPE;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication required' USING ERRCODE = '42501';
    END IF;

    DELETE FROM public.account_deletion_reservations
    WHERE user_id = current_user_id
    RETURNING * INTO reservation_row;

    IF reservation_row.user_id IS NOT NULL THEN
        UPDATE public.families
        SET invite_active = reservation_row.invite_was_active
        WHERE id = reservation_row.family_id
          AND head_user_id = current_user_id;
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION public.export_my_data()
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, auth
AS $$
DECLARE
    current_user_id UUID := auth.uid();
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication required' USING ERRCODE = '42501';
    END IF;

    RETURN jsonb_build_object(
        'exportedAt', now(),
        'account', (
            SELECT jsonb_build_object(
                'id', u.id,
                'email', u.email,
                'createdAt', u.created_at,
                'lastSignInAt', u.last_sign_in_at
            )
            FROM auth.users u
            WHERE u.id = current_user_id
        ),
        'familyMembership', coalesce((
            SELECT jsonb_agg(to_jsonb(fm))
            FROM public.family_members fm
            WHERE fm.user_id = current_user_id
        ), '[]'::jsonb),
        'expenses', coalesce((
            SELECT jsonb_agg(to_jsonb(e) ORDER BY e.created_at)
            FROM public.expenses e
            WHERE e.user_id = current_user_id
        ), '[]'::jsonb),
        'budgets', coalesce((
            SELECT jsonb_agg(to_jsonb(b) ORDER BY b.created_at)
            FROM public.budgets b
            WHERE b.user_id = current_user_id
        ), '[]'::jsonb),
        'savingsGoals', coalesce((
            SELECT jsonb_agg(to_jsonb(s) ORDER BY s.created_at)
            FROM public.savings_goals s
            WHERE s.user_id = current_user_id
        ), '[]'::jsonb),
        'emis', coalesce((
            SELECT jsonb_agg(to_jsonb(e) ORDER BY e.created_at)
            FROM public.emis e
            WHERE e.user_id = current_user_id
        ), '[]'::jsonb),
        'notifications', coalesce((
            SELECT jsonb_agg(to_jsonb(n) ORDER BY n.created_at)
            FROM public.notifications n
            WHERE n.user_id = current_user_id
        ), '[]'::jsonb),
        'financialAccounts', coalesce((
            SELECT jsonb_agg(to_jsonb(a))
            FROM public.financial_accounts a
            WHERE a.user_id = current_user_id
        ), '[]'::jsonb),
        'providerConnections', coalesce((
            SELECT jsonb_agg(to_jsonb(c) - 'credential_secret_ref' - 'sync_cursor')
            FROM public.provider_connections c
            WHERE c.user_id = current_user_id
        ), '[]'::jsonb),
        'importRuns', coalesce((
            SELECT jsonb_agg(to_jsonb(i))
            FROM public.import_runs i
            WHERE i.user_id = current_user_id
        ), '[]'::jsonb),
        'rawTransactions', coalesce((
            SELECT jsonb_agg(to_jsonb(r))
            FROM public.raw_transactions r
            WHERE r.user_id = current_user_id
        ), '[]'::jsonb),
        'financeHubTransactions', coalesce((
            SELECT jsonb_agg(to_jsonb(t))
            FROM public.financehub_transactions t
            WHERE t.user_id = current_user_id
        ), '[]'::jsonb),
        'transactionMatches', coalesce((
            SELECT jsonb_agg(to_jsonb(m))
            FROM public.transaction_matches m
            WHERE m.user_id = current_user_id
        ), '[]'::jsonb),
        'categorizationRules', coalesce((
            SELECT jsonb_agg(to_jsonb(r))
            FROM public.categorization_rules r
            WHERE r.user_id = current_user_id
        ), '[]'::jsonb),
        'transactionAudit', coalesce((
            SELECT jsonb_agg(to_jsonb(a) ORDER BY a.changed_at)
            FROM public.financehub_transaction_audit a
            WHERE a.user_id = current_user_id
        ), '[]'::jsonb)
    );
END;
$$;

REVOKE ALL ON FUNCTION public.get_account_deletion_status() FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_account_deletion_status() FROM anon;
REVOKE ALL ON FUNCTION public.export_my_data() FROM PUBLIC;
REVOKE ALL ON FUNCTION public.export_my_data() FROM anon;
REVOKE ALL ON FUNCTION public.begin_account_deletion() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION public.cancel_account_deletion() FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION public.get_account_deletion_status() TO authenticated;
GRANT EXECUTE ON FUNCTION public.begin_account_deletion() TO authenticated;
GRANT EXECUTE ON FUNCTION public.cancel_account_deletion() TO authenticated;
GRANT EXECUTE ON FUNCTION public.export_my_data() TO authenticated;
