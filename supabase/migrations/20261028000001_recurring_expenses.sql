-- Track recurrence series explicitly so month-end expenses roll forward reliably.
ALTER TABLE public.expenses
    ADD COLUMN IF NOT EXISTS recurrence_series_id UUID,
    ADD COLUMN IF NOT EXISTS recurrence_day SMALLINT
        CHECK (recurrence_day BETWEEN 1 AND 31);

-- Group copies created by the original recurring job into the same best-effort series.
UPDATE public.expenses
SET recurrence_series_id = md5(
        user_id::text || '|' ||
        family_id::text || '|' ||
        category_id::text || '|' ||
        amount::text || '|' ||
        COALESCE(note, '') || '|' ||
        EXTRACT(DAY FROM date)::text
    )::uuid,
    recurrence_day = EXTRACT(DAY FROM date)::smallint
WHERE is_recurring = true
  AND recurrence_series_id IS NULL;

CREATE OR REPLACE FUNCTION public.set_expense_recurrence_metadata()
RETURNS TRIGGER
LANGUAGE plpgsql
SET search_path = public
AS $$
BEGIN
    IF NEW.is_recurring = true THEN
        NEW.recurrence_series_id := COALESCE(NEW.recurrence_series_id, NEW.id);
        NEW.recurrence_day := COALESCE(
            NEW.recurrence_day,
            EXTRACT(DAY FROM NEW.date)::smallint
        );
    END IF;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS set_expense_recurrence_metadata ON public.expenses;
CREATE TRIGGER set_expense_recurrence_metadata
BEFORE INSERT OR UPDATE OF is_recurring, date
ON public.expenses
FOR EACH ROW
EXECUTE FUNCTION public.set_expense_recurrence_metadata();

CREATE OR REPLACE FUNCTION public.process_recurring_expenses()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public
AS $$
BEGIN
    WITH latest_in_series AS (
        SELECT DISTINCT ON (e.recurrence_series_id)
            e.*
        FROM public.expenses e
        WHERE e.recurrence_series_id IS NOT NULL
        ORDER BY
            e.recurrence_series_id,
            e.date DESC,
            e.created_at DESC,
            e.id DESC
    ),
    due_expenses AS (
        SELECT
            e.*,
            (
                date_trunc('month', CURRENT_DATE)::date
                + (
                    LEAST(
                        e.recurrence_day,
                        EXTRACT(
                            DAY FROM (
                                date_trunc('month', CURRENT_DATE)
                                + INTERVAL '1 month - 1 day'
                            )
                        )::smallint
                    ) - 1
                )
            )::date AS target_date
        FROM latest_in_series e
        WHERE e.is_recurring = true
          AND e.deleted_at IS NULL
          AND e.recurrence_day IS NOT NULL
    )
    INSERT INTO public.expenses (
        user_id,
        family_id,
        category_id,
        amount,
        date,
        note,
        is_recurring,
        receipt_url,
        recurrence_series_id,
        recurrence_day,
        created_at
    )
    SELECT
        e.user_id,
        e.family_id,
        e.category_id,
        e.amount,
        e.target_date,
        e.note,
        true,
        NULL,
        e.recurrence_series_id,
        e.recurrence_day,
        NOW()
    FROM due_expenses e
    WHERE CURRENT_DATE >= e.target_date
      AND e.date < e.target_date
      AND NOT EXISTS (
          SELECT 1
          FROM public.expenses existing
          WHERE existing.recurrence_series_id = e.recurrence_series_id
            AND existing.date = e.target_date
      );
END;
$$;

COMMENT ON COLUMN public.expenses.recurrence_series_id IS
    'Stable identifier shared by all generated copies of a recurring expense.';
COMMENT ON COLUMN public.expenses.recurrence_day IS
    'Preferred day of month; clamped to the final day for shorter months.';
