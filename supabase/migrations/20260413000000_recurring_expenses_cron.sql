CREATE EXTENSION IF NOT EXISTS pg_cron WITH SCHEMA extensions;

CREATE OR REPLACE FUNCTION public.process_recurring_expenses()
RETURNS void AS $$
BEGIN
    INSERT INTO public.expenses (user_id, family_id, category_id, amount, date, note, is_recurring, receipt_url, created_at)
    SELECT e.user_id, e.family_id, e.category_id, e.amount, CURRENT_DATE, e.note, true, e.receipt_url, NOW()
    FROM public.expenses e
    WHERE e.is_recurring = true
      AND e.deleted_at IS NULL
      AND e.date = (CURRENT_DATE - INTERVAL '1 month')::date
      AND NOT EXISTS (
          SELECT 1 FROM public.expenses e2
          WHERE e2.user_id = e.user_id
            AND e2.category_id = e.category_id
            AND e2.amount = e.amount
            AND e2.date = CURRENT_DATE
            AND e2.is_recurring = true
            AND e2.deleted_at IS NULL
      );
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

-- Run daily at 1 AM UTC
SELECT cron.schedule('process-daily-recurring', '0 1 * * *', 'SELECT public.process_recurring_expenses()');
