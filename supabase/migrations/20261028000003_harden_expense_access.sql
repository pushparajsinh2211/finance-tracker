-- Keep expense ownership, family membership, and category assignment consistent.
-- This does not add any bank, card, or UPI provider integration.

DO $$
DECLARE
    existing_policy RECORD;
BEGIN
    FOR existing_policy IN
        SELECT policyname
        FROM pg_policies
        WHERE schemaname = 'public'
          AND tablename = 'expenses'
    LOOP
        EXECUTE format(
            'DROP POLICY %I ON public.expenses',
            existing_policy.policyname
        );
    END LOOP;
END
$$;

CREATE POLICY "Users manage own current-family expenses"
    ON public.expenses
    FOR ALL
    TO authenticated
    USING (
        user_id = auth.uid()
        AND family_id = public.get_my_family_id()
    )
    WITH CHECK (
        user_id = auth.uid()
        AND family_id = public.get_my_family_id()
        AND EXISTS (
            SELECT 1
            FROM public.categories AS category
            WHERE category.id = expenses.category_id
              AND category.family_id = expenses.family_id
              AND NOT category.is_archived
        )
    );

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.expenses'::regclass
          AND conname = 'expenses_amount_positive'
    ) THEN
        ALTER TABLE public.expenses
            ADD CONSTRAINT expenses_amount_positive
            CHECK (amount > 0) NOT VALID;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'public.expenses'::regclass
          AND conname = 'expenses_note_length'
    ) THEN
        ALTER TABLE public.expenses
            ADD CONSTRAINT expenses_note_length
            CHECK (note IS NULL OR char_length(note) <= 200) NOT VALID;
    END IF;
END
$$;
