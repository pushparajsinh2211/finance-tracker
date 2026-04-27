-- Create families table
CREATE TABLE public.families (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name TEXT NOT NULL,
    head_user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    invite_code TEXT UNIQUE NOT NULL,
    invite_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Create family_members table
CREATE TABLE public.family_members (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    family_id UUID NOT NULL REFERENCES public.families(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    display_name TEXT NOT NULL,
    relation TEXT,
    is_dependent BOOLEAN DEFAULT true,
    joined_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    UNIQUE (family_id, user_id)
);

-- Create categories table
CREATE TABLE public.categories (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    family_id UUID REFERENCES public.families(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    color TEXT,
    is_default BOOLEAN DEFAULT false,
    is_archived BOOLEAN DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Create expenses table
CREATE TABLE public.expenses (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    family_id UUID NOT NULL REFERENCES public.families(id) ON DELETE CASCADE,
    category_id UUID NOT NULL REFERENCES public.categories(id) ON DELETE CASCADE,
    amount NUMERIC NOT NULL,
    date DATE DEFAULT CURRENT_DATE,
    note TEXT,
    is_recurring BOOLEAN DEFAULT false,
    receipt_url TEXT,
    deleted_at TIMESTAMP WITH TIME ZONE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Create budgets table
CREATE TABLE public.budgets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    category_id UUID NOT NULL REFERENCES public.categories(id) ON DELETE CASCADE,
    month TEXT NOT NULL, -- Format YYYY-MM
    amount NUMERIC NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    UNIQUE (user_id, category_id, month)
);

-- Create savings_goals table
CREATE TABLE public.savings_goals (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    target_amount NUMERIC NOT NULL,
    current_amount NUMERIC DEFAULT 0,
    deadline DATE,
    is_completed BOOLEAN DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Create emis table
CREATE TABLE public.emis (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    lender_name TEXT NOT NULL,
    principal NUMERIC NOT NULL,
    monthly_emi NUMERIC NOT NULL,
    start_date DATE NOT NULL,
    tenure_months INTEGER NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Create notifications table
CREATE TABLE public.notifications (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    type TEXT NOT NULL,
    message TEXT NOT NULL,
    is_read BOOLEAN DEFAULT false,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Row Level Security (RLS) policies

ALTER TABLE public.families ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.family_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.categories ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.expenses ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.budgets ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.savings_goals ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.emis ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.notifications ENABLE ROW LEVEL SECURITY;

-- Helper function to fetch user's family ID without triggering RLS recursion
CREATE OR REPLACE FUNCTION public.get_my_family_id()
RETURNS UUID AS $$
    SELECT family_id FROM public.family_members WHERE user_id = auth.uid() LIMIT 1;
$$ LANGUAGE sql SECURITY DEFINER SET search_path = public;

-- Families: head can view entirely, members can view their family
CREATE POLICY "Families are viewable by members" ON public.families FOR SELECT
    USING (id = public.get_my_family_id() OR head_user_id = auth.uid());

CREATE POLICY "Head can manage their families" ON public.families FOR ALL
    USING (head_user_id = auth.uid());

-- Family members
CREATE POLICY "Family members viewable by anyone in same family" ON public.family_members FOR SELECT
    USING (family_id = public.get_my_family_id() OR family_id IN (SELECT id FROM public.families WHERE head_user_id = auth.uid()));

CREATE POLICY "Head can delete family members" ON public.family_members FOR DELETE
    USING (family_id IN (SELECT id FROM public.families WHERE head_user_id = auth.uid()));

CREATE POLICY "Head can update family members" ON public.family_members FOR UPDATE
    USING (family_id IN (SELECT id FROM public.families WHERE head_user_id = auth.uid()));

CREATE POLICY "Head can insert family members" ON public.family_members FOR INSERT
    WITH CHECK (family_id IN (SELECT id FROM public.families WHERE head_user_id = auth.uid()));
    
CREATE POLICY "Users can insert themselves into family_members" ON public.family_members FOR INSERT
    WITH CHECK (user_id = auth.uid());

-- Categories
CREATE POLICY "Categories viewable by family members" ON public.categories FOR SELECT
    USING (family_id IS NULL OR family_id = public.get_my_family_id() OR family_id IN (SELECT id FROM public.families WHERE head_user_id = auth.uid()));

CREATE POLICY "Head can manage family categories" ON public.categories FOR ALL
    USING (family_id IN (SELECT id FROM public.families WHERE head_user_id = auth.uid()));
    
-- Expenses
CREATE POLICY "Users manage their own expenses" ON public.expenses FOR ALL
    USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

-- Budgets
CREATE POLICY "Users manage their own budgets" ON public.budgets FOR ALL
    USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

-- Savings Goals
CREATE POLICY "Users manage their own savings goals" ON public.savings_goals FOR ALL
    USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

-- EMIs
CREATE POLICY "Users manage their own emis" ON public.emis FOR ALL
    USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

-- Notifications
CREATE POLICY "Users manage their own notifications" ON public.notifications FOR ALL
    USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());


-- DB Function for aggregating dependent expenses securely
CREATE OR REPLACE FUNCTION public.get_dependent_expenses_summary(target_family_id UUID)
RETURNS TABLE (
    member_user_id UUID,
    category_id UUID,
    total_amount NUMERIC
) AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM public.families WHERE id = target_family_id AND head_user_id = auth.uid()) THEN
        RAISE EXCEPTION 'Not authorized. Head access required.';
    END IF;

    RETURN QUERY
    SELECT 
        e.user_id,
        e.category_id,
        SUM(e.amount) as total_amount
    FROM public.expenses e
    JOIN public.family_members fm ON e.user_id = fm.user_id AND e.family_id = fm.family_id
    WHERE e.family_id = target_family_id
      AND fm.is_dependent = true
      AND e.deleted_at IS NULL
    GROUP BY e.user_id, e.category_id;
END;
$$ LANGUAGE plpgsql SECURITY DEFINER;
