-- Enforce the one-family-per-user invariant and keep invite codes head-only.

DO $$
BEGIN
    IF EXISTS (
        SELECT user_id
        FROM public.family_members
        GROUP BY user_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION
            'Cannot enforce one family per user: duplicate family memberships exist';
    END IF;
END;
$$;

CREATE UNIQUE INDEX IF NOT EXISTS family_members_one_family_per_user_idx
    ON public.family_members (user_id);

CREATE OR REPLACE FUNCTION public.get_my_family_id()
RETURNS UUID
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT family_id
    FROM public.family_members
    WHERE user_id = auth.uid()
    LIMIT 1;
$$;

CREATE OR REPLACE FUNCTION public.is_family_head(p_family_id UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.families
        WHERE id = p_family_id
          AND head_user_id = auth.uid()
    );
$$;

DROP POLICY IF EXISTS "Families are viewable by members" ON public.families;
DROP POLICY IF EXISTS "Head can insert family members" ON public.family_members;
DROP POLICY IF EXISTS "Users can insert themselves into family_members" ON public.family_members;
DROP POLICY IF EXISTS "Head can delete family members" ON public.family_members;
DROP POLICY IF EXISTS "Head can update family members" ON public.family_members;

CREATE POLICY "Head can create own family membership"
ON public.family_members FOR INSERT
TO authenticated
WITH CHECK (
    user_id = auth.uid()
    AND lower(COALESCE(relation, '')) = 'head'
    AND public.is_family_head(family_id)
);

CREATE POLICY "Head can update non-head members"
ON public.family_members FOR UPDATE
TO authenticated
USING (
    public.is_family_head(family_id)
    AND user_id <> auth.uid()
    AND lower(COALESCE(relation, '')) <> 'head'
)
WITH CHECK (
    public.is_family_head(family_id)
    AND user_id <> auth.uid()
    AND lower(COALESCE(relation, '')) <> 'head'
);

CREATE POLICY "Head can delete non-head members"
ON public.family_members FOR DELETE
TO authenticated
USING (
    public.is_family_head(family_id)
    AND user_id <> auth.uid()
    AND lower(COALESCE(relation, '')) <> 'head'
);

CREATE OR REPLACE FUNCTION public.protect_family_member_identity()
RETURNS TRIGGER
LANGUAGE plpgsql
SET search_path = public
AS $$
BEGIN
    IF NEW.family_id IS DISTINCT FROM OLD.family_id
       OR NEW.user_id IS DISTINCT FROM OLD.user_id
       OR NEW.relation IS DISTINCT FROM OLD.relation THEN
        RAISE EXCEPTION 'Family member identity and role cannot be changed';
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS protect_family_member_identity ON public.family_members;
CREATE TRIGGER protect_family_member_identity
BEFORE UPDATE ON public.family_members
FOR EACH ROW
EXECUTE FUNCTION public.protect_family_member_identity();

CREATE OR REPLACE FUNCTION public.join_family_by_code(
    p_invite_code TEXT,
    p_display_name TEXT
)
RETURNS UUID
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
DECLARE
    caller_user_id UUID := auth.uid();
    found_family_id UUID;
    normalized_name TEXT := btrim(COALESCE(p_display_name, ''));
BEGIN
    IF caller_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication required' USING ERRCODE = '42501';
    END IF;

    IF char_length(normalized_name) < 1 OR char_length(normalized_name) > 80 THEN
        RAISE EXCEPTION 'Display name must be between 1 and 80 characters';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM public.family_members
        WHERE user_id = caller_user_id
    ) THEN
        RAISE EXCEPTION 'User already belongs to a family';
    END IF;

    SELECT id
    INTO found_family_id
    FROM public.families
    WHERE invite_code = upper(btrim(COALESCE(p_invite_code, '')))
      AND invite_active = true
    FOR UPDATE;

    IF found_family_id IS NULL THEN
        RAISE EXCEPTION 'Invalid or inactive invite code';
    END IF;

    INSERT INTO public.family_members (
        family_id,
        user_id,
        display_name,
        relation,
        is_dependent
    )
    VALUES (
        found_family_id,
        caller_user_id,
        normalized_name,
        'Member',
        true
    );

    RETURN found_family_id;
EXCEPTION
    WHEN unique_violation THEN
        RAISE EXCEPTION 'User already belongs to a family';
END;
$$;

CREATE OR REPLACE FUNCTION public.get_my_family_details()
RETURNS JSONB
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT jsonb_build_object(
        'id', f.id,
        'name', f.name,
        'inviteCode', CASE
            WHEN f.head_user_id = auth.uid() THEN f.invite_code
            ELSE ''
        END,
        'headUserId', f.head_user_id
    )
    FROM public.family_members fm
    JOIN public.families f ON f.id = fm.family_id
    WHERE fm.user_id = auth.uid()
    LIMIT 1;
$$;

REVOKE ALL ON FUNCTION public.join_family_by_code(TEXT, TEXT) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_my_family_details() FROM PUBLIC;
REVOKE ALL ON FUNCTION public.get_dependent_expenses_summary(UUID) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION public.join_family_by_code(TEXT, TEXT) TO authenticated;
GRANT EXECUTE ON FUNCTION public.get_my_family_details() TO authenticated;
GRANT EXECUTE ON FUNCTION public.get_dependent_expenses_summary(UUID) TO authenticated;
