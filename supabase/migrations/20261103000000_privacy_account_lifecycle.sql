-- User privacy export and safe account-deletion preflight.
-- Authenticated callers can only inspect their own data.

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
    END IF;

    RETURN jsonb_build_object(
        'canDelete', NOT (is_head AND member_count > 1),
        'isFamilyHead', is_head,
        'familyId', membership_row.family_id,
        'familyMemberCount', member_count,
        'blocker', CASE
            WHEN is_head AND member_count > 1
                THEN 'Transfer family ownership or remove the other members before deleting this account.'
            ELSE NULL
        END
    );
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
REVOKE ALL ON FUNCTION public.export_my_data() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.get_account_deletion_status() TO authenticated;
GRANT EXECUTE ON FUNCTION public.export_my_data() TO authenticated;
