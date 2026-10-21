-- FinanceHub aggregation foundation.
-- External bank/card/UPI providers are not implemented by this migration.

CREATE TABLE public.financial_accounts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    family_id UUID REFERENCES public.families(id) ON DELETE CASCADE,
    source_type TEXT NOT NULL CHECK (source_type IN ('bank', 'credit_card', 'upi', 'cash')),
    provider_name TEXT,
    provider_account_ref TEXT,
    display_name TEXT NOT NULL,
    institution_name TEXT,
    masked_identifier TEXT,
    currency CHAR(3) NOT NULL DEFAULT 'INR',
    status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'action_required', 'revoked', 'archived')),
    last_synced_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (user_id, provider_name, provider_account_ref)
);

CREATE TABLE public.provider_connections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    provider_name TEXT NOT NULL,
    provider_connection_ref TEXT NOT NULL,
    consent_ref TEXT,
    consent_status TEXT NOT NULL CHECK (consent_status IN ('pending', 'active', 'expired', 'revoked', 'failed')),
    consent_expires_at TIMESTAMPTZ,
    credential_secret_ref TEXT,
    sync_cursor TEXT,
    last_synced_at TIMESTAMPTZ,
    last_error_code TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (provider_name, provider_connection_ref)
);

CREATE TABLE public.import_runs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    connection_id UUID REFERENCES public.provider_connections(id) ON DELETE SET NULL,
    source_kind TEXT NOT NULL CHECK (source_kind IN ('provider', 'csv', 'pdf', 'manual')),
    idempotency_key TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'queued' CHECK (status IN ('queued', 'processing', 'completed', 'partial', 'failed', 'action_required')),
    cursor_before TEXT,
    cursor_after TEXT,
    received_count INTEGER NOT NULL DEFAULT 0,
    imported_count INTEGER NOT NULL DEFAULT 0,
    duplicate_count INTEGER NOT NULL DEFAULT 0,
    review_count INTEGER NOT NULL DEFAULT 0,
    error_code TEXT,
    error_message TEXT,
    started_at TIMESTAMPTZ,
    completed_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (user_id, idempotency_key)
);

CREATE TABLE public.raw_transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    import_run_id UUID NOT NULL REFERENCES public.import_runs(id) ON DELETE CASCADE,
    connection_id UUID REFERENCES public.provider_connections(id) ON DELETE SET NULL,
    provider_name TEXT,
    external_transaction_ref TEXT,
    payload JSONB NOT NULL,
    payload_checksum TEXT NOT NULL,
    received_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (user_id, connection_id, external_transaction_ref),
    UNIQUE (user_id, import_run_id, payload_checksum)
);

CREATE TABLE public.financehub_transactions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    family_id UUID REFERENCES public.families(id) ON DELETE CASCADE,
    account_id UUID NOT NULL REFERENCES public.financial_accounts(id) ON DELETE CASCADE,
    raw_transaction_id UUID REFERENCES public.raw_transactions(id) ON DELETE SET NULL,
    category_id UUID REFERENCES public.categories(id) ON DELETE SET NULL,
    external_transaction_ref TEXT,
    payment_reference TEXT,
    payment_rail TEXT,
    merchant_name TEXT,
    narration TEXT,
    amount NUMERIC(18,2) NOT NULL CHECK (amount >= 0),
    currency CHAR(3) NOT NULL DEFAULT 'INR',
    direction TEXT NOT NULL CHECK (direction IN ('debit', 'credit')),
    transaction_type TEXT NOT NULL CHECK (transaction_type IN ('purchase', 'income', 'transfer', 'fee', 'refund', 'cash', 'adjustment')),
    status TEXT NOT NULL DEFAULT 'posted' CHECK (status IN ('pending', 'posted', 'reversed')),
    occurred_at TIMESTAMPTZ NOT NULL,
    posted_at TIMESTAMPTZ,
    fingerprint TEXT NOT NULL,
    category_source TEXT CHECK (category_source IN ('user', 'rule', 'suggestion', 'provider')),
    category_confidence NUMERIC(5,4) CHECK (category_confidence BETWEEN 0 AND 1),
    needs_review BOOLEAN NOT NULL DEFAULT false,
    linked_transaction_id UUID REFERENCES public.financehub_transactions(id) ON DELETE SET NULL,
    deleted_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (account_id, external_transaction_ref)
);

CREATE INDEX ix_financehub_transactions_user_occurred
    ON public.financehub_transactions (user_id, occurred_at DESC)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_financehub_transactions_fingerprint
    ON public.financehub_transactions (account_id, fingerprint);

CREATE TABLE public.transaction_matches (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    transaction_id UUID NOT NULL REFERENCES public.financehub_transactions(id) ON DELETE CASCADE,
    candidate_transaction_id UUID NOT NULL REFERENCES public.financehub_transactions(id) ON DELETE CASCADE,
    match_type TEXT NOT NULL CHECK (match_type IN ('duplicate', 'transfer', 'card_payment')),
    score NUMERIC(5,4) NOT NULL CHECK (score BETWEEN 0 AND 1),
    reasons JSONB NOT NULL DEFAULT '[]'::jsonb,
    status TEXT NOT NULL DEFAULT 'suggested' CHECK (status IN ('suggested', 'confirmed', 'rejected')),
    reviewed_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CHECK (transaction_id <> candidate_transaction_id),
    UNIQUE (transaction_id, candidate_transaction_id, match_type)
);

CREATE TABLE public.categorization_rules (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    priority INTEGER NOT NULL DEFAULT 100,
    enabled BOOLEAN NOT NULL DEFAULT true,
    match_definition JSONB NOT NULL,
    category_id UUID NOT NULL REFERENCES public.categories(id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE public.financial_accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.provider_connections ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.import_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.raw_transactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.financehub_transactions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.transaction_matches ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.categorization_rules ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users manage own financial accounts" ON public.financial_accounts
    FOR ALL USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users manage own provider connections" ON public.provider_connections
    FOR ALL USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users manage own import runs" ON public.import_runs
    FOR ALL USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users manage own raw transactions" ON public.raw_transactions
    FOR ALL USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users manage own FinanceHub transactions" ON public.financehub_transactions
    FOR ALL USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users manage own transaction matches" ON public.transaction_matches
    FOR ALL USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

CREATE POLICY "Users manage own categorization rules" ON public.categorization_rules
    FOR ALL USING (user_id = auth.uid()) WITH CHECK (user_id = auth.uid());

COMMENT ON COLUMN public.provider_connections.credential_secret_ref IS
    'Reference to a server-side secret. Never store provider credentials or tokens in this table.';
