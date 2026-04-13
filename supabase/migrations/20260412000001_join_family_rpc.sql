CREATE OR REPLACE FUNCTION public.join_family_by_code(
    p_invite_code TEXT,
    p_display_name TEXT
)
RETURNS UUID AS $$
DECLARE
    found_family_id UUID;
    existing_family UUID;
BEGIN
    SELECT id INTO found_family_id
    FROM public.families
    WHERE invite_code = p_invite_code AND invite_active = true;

    IF found_family_id IS NULL THEN
        RAISE EXCEPTION 'Invalid or inactive invite code';
    END IF;

    SELECT family_id INTO existing_family
    FROM public.family_members
    WHERE user_id = auth.uid();

    IF existing_family IS NOT NULL THEN
        RAISE EXCEPTION 'User already belongs to a family';
    END IF;

    INSERT INTO public.family_members (family_id, user_id, display_name, relation, is_dependent)
    VALUES (found_family_id, auth.uid(), p_display_name, 'Member', true);

    RETURN found_family_id;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;
