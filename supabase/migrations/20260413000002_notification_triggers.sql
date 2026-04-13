-- Trigger 1: Notify Family Head when a new member joins
CREATE OR REPLACE FUNCTION notify_member_joined()
RETURNS TRIGGER AS $$
DECLARE
    v_head_id UUID;
    v_family_name TEXT;
BEGIN
    SELECT head_user_id, name INTO v_head_id, v_family_name 
    FROM public.families WHERE id = NEW.family_id;

    -- Don't notify the head if they are the ones joining (during family creation)
    IF NEW.user_id != v_head_id THEN
        INSERT INTO public.notifications (user_id, type, message)
        VALUES (v_head_id, 'Member Joined', NEW.display_name || ' joined ' || v_family_name || '!');
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

CREATE TRIGGER on_member_joined
AFTER INSERT ON public.family_members
FOR EACH ROW
EXECUTE FUNCTION notify_member_joined();


-- Trigger 2: Notify User when budget is exceeded
CREATE OR REPLACE FUNCTION check_budget_exceeded()
RETURNS TRIGGER AS $$
DECLARE
    v_spent NUMERIC;
    v_budget NUMERIC;
    v_category_name TEXT;
    v_month TEXT;
BEGIN
    -- Only check if expense count > 0, calculate sum for the month
    v_month := to_char(NEW.date, 'YYYY-MM');

    -- Get budget for this user, category, and month
    SELECT amount INTO v_budget FROM public.budgets 
    WHERE user_id = NEW.user_id AND category_id = NEW.category_id AND month = v_month;

    IF FOUND THEN
        -- Get total spent
        SELECT COALESCE(SUM(amount), 0) INTO v_spent FROM public.expenses
        WHERE user_id = NEW.user_id 
          AND category_id = NEW.category_id 
          AND to_char(date, 'YYYY-MM') = v_month
          AND deleted_at IS NULL;

        IF v_spent > v_budget THEN
            -- Check if we already notified them recently to avoid spam (e.g. only 1 budget alert per category per month)
            IF NOT EXISTS (
                SELECT 1 FROM public.notifications 
                WHERE user_id = NEW.user_id 
                  AND type = 'Budget Exceeded' 
                  AND message LIKE '%' || v_month || '%'
                  AND message LIKE '%' || (SELECT name FROM public.categories WHERE id = NEW.category_id) || '%'
            ) THEN
                SELECT name INTO v_category_name FROM public.categories WHERE id = NEW.category_id;
                INSERT INTO public.notifications (user_id, type, message)
                VALUES (NEW.user_id, 'Budget Exceeded', 'You have exceeded your budget for ' || v_category_name || ' in ' || v_month || '!');
            END IF;
        END IF;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;

CREATE TRIGGER on_expense_budget_check
AFTER INSERT OR UPDATE OF amount, date, deleted_at ON public.expenses
FOR EACH ROW
EXECUTE FUNCTION check_budget_exceeded();
