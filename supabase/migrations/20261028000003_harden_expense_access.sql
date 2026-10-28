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
        )
    );

CREATE OR REPLACE FUNCTION public.validate_expense_category_assignment()
RETURNS trigger
LANGUAGE plpgsql
SET search_path = public
AS $$
BEGIN
    IF TG_OP = 'INSERT'
       OR NEW.category_id IS DISTINCT FROM OLD.category_id
       OR NEW.family_id IS DISTINCT FROM OLD.family_id THEN
        IF NOT EXISTS (
            SELECT 1
            FROM public.categories AS category
            WHERE category.id = NEW.category_id
              AND category.family_id = NEW.family_id
              AND NOT category.is_archived
        ) THEN
            RAISE EXCEPTION 'Expense category must be active and belong to the expense family'
                USING ERRCODE = '23514';
        END IF;
    END IF;

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS validate_expense_category_assignment
    ON public.expenses;

CREATE TRIGGER validate_expense_category_assignment
    BEFORE INSERT OR UPDATE OF category_id, family_id
    ON public.expenses
    FOR EACH ROW
    EXECUTE FUNCTION public.validate_expense_category_assignment();

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
