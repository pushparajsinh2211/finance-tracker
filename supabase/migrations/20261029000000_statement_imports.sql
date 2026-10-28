-- User-initiated statement import pipeline.
-- Automatic bank/card/UPI provider connectivity is still not enabled.

CREATE UNIQUE INDEX IF NOT EXISTS ux_financehub_transactions_account_fingerprint_active
    ON public.financehub_transactions (account_id, fingerprint)
    WHERE deleted_at IS NULL;

DO $$
DECLARE
    target_table TEXT;
    existing_policy RECORD;
BEGIN
    FOREACH target_table IN ARRAY ARRAY[
        'financial_accounts',
        'import_runs',
        'raw_transactions',
        'financehub_transactions'
    ]
    LOOP
        FOR existing_policy IN
            SELECT policyname
            FROM pg_policies
            WHERE schemaname = 'public'
              AND tablename = target_table
        LOOP
            EXECUTE format(
                'DROP POLICY %I ON public.%I',
                existing_policy.policyname,
                target_table
            );
        END LOOP;
    END LOOP;
END
$$;

CREATE POLICY "Users read own financial accounts"
    ON public.financial_accounts
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users read own import runs"
    ON public.import_runs
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users read own immutable raw transactions"
    ON public.raw_transactions
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE POLICY "Users read own canonical transactions"
    ON public.financehub_transactions
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE OR REPLACE FUNCTION public.import_financehub_statement(
    p_source_type TEXT,
    p_file_type TEXT,
    p_account_name TEXT,
    p_masked_identifier TEXT,
    p_idempotency_key TEXT,
    p_transactions JSONB
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, extensions
AS $$
DECLARE
    current_user_id UUID := auth.uid();
    current_family_id UUID;
    account_id_value UUID;
    import_id_value UUID;
    raw_id_value UUID;
    transaction_item JSONB;
    transaction_fingerprint TEXT;
    transaction_checksum TEXT;
    imported_count_value INTEGER := 0;
    duplicate_count_value INTEGER := 0;
    received_count_value INTEGER := 0;
    review_count_value INTEGER := 0;
    existing_run public.import_runs%ROWTYPE;
    external_reference_value TEXT;
    occurred_at_value TIMESTAMPTZ;
    amount_value NUMERIC(18,2);
    direction_value TEXT;
    narration_value TEXT;
    payment_rail_value TEXT;
    transaction_type_value TEXT;
    needs_review_value BOOLEAN;
    occurrence_index_value INTEGER;
    account_reference_value TEXT;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication is required' USING ERRCODE = '42501';
    END IF;

    IF p_source_type NOT IN ('bank', 'credit_card') THEN
        RAISE EXCEPTION 'Source type must be bank or credit_card' USING ERRCODE = '22023';
    END IF;

    IF p_file_type NOT IN ('csv', 'pdf') THEN
        RAISE EXCEPTION 'File type must be csv or pdf' USING ERRCODE = '22023';
    END IF;

    IF p_account_name IS NULL
       OR char_length(btrim(p_account_name)) = 0
       OR char_length(btrim(p_account_name)) > 80 THEN
        RAISE EXCEPTION 'Account name must contain 1 to 80 characters' USING ERRCODE = '22023';
    END IF;

    IF p_masked_identifier IS NOT NULL
       AND (
           char_length(p_masked_identifier) > 32
           OR p_masked_identifier ~ '[0-9]{5,}'
       ) THEN
        RAISE EXCEPTION 'Only a masked identifier or last four digits may be stored' USING ERRCODE = '22023';
    END IF;

    IF p_idempotency_key IS NULL OR char_length(p_idempotency_key) <> 64 THEN
        RAISE EXCEPTION 'A SHA-256 idempotency key is required' USING ERRCODE = '22023';
    END IF;

    IF p_transactions IS NULL
       OR jsonb_typeof(p_transactions) <> 'array'
       OR jsonb_array_length(p_transactions) = 0
       OR jsonb_array_length(p_transactions) > 5000 THEN
        RAISE EXCEPTION 'Transactions must be a non-empty array of at most 5000 rows'
            USING ERRCODE = '22023';
    END IF;

    SELECT *
    INTO existing_run
    FROM public.import_runs
    WHERE user_id = current_user_id
      AND idempotency_key = p_idempotency_key;

    IF FOUND THEN
        RETURN jsonb_build_object(
            'importId', existing_run.id,
            'status', existing_run.status,
            'alreadyImported', true,
            'receivedCount', existing_run.received_count,
            'importedCount', existing_run.imported_count,
            'duplicateCount', existing_run.duplicate_count,
            'reviewCount', existing_run.review_count
        );
    END IF;

    current_family_id := public.get_my_family_id();
    account_reference_value := 'statement:' || encode(
        digest(
            lower(
                p_source_type || '|' ||
                btrim(p_account_name) || '|' ||
                coalesce(p_masked_identifier, '')
            ),
            'sha256'
        ),
        'hex'
    );

    INSERT INTO public.financial_accounts (
        user_id,
        family_id,
        source_type,
        provider_name,
        provider_account_ref,
        display_name,
        masked_identifier,
        currency,
        status,
        last_synced_at,
        updated_at
    )
    VALUES (
        current_user_id,
        current_family_id,
        p_source_type,
        'statement_import',
        account_reference_value,
        btrim(p_account_name),
        nullif(btrim(p_masked_identifier), ''),
        'INR',
        'active',
        now(),
        now()
    )
    ON CONFLICT (user_id, provider_name, provider_account_ref)
    DO UPDATE SET
        display_name = EXCLUDED.display_name,
        masked_identifier = EXCLUDED.masked_identifier,
        last_synced_at = now(),
        updated_at = now()
    RETURNING id INTO account_id_value;

    INSERT INTO public.import_runs (
        user_id,
        source_kind,
        idempotency_key,
        status,
        started_at
    )
    VALUES (
        current_user_id,
        p_file_type,
        p_idempotency_key,
        'processing',
        now()
    )
    RETURNING id INTO import_id_value;

    FOR transaction_item IN
        SELECT value FROM jsonb_array_elements(p_transactions)
    LOOP
        received_count_value := received_count_value + 1;

        BEGIN
            occurred_at_value := (transaction_item ->> 'occurredAt')::timestamptz;
            amount_value := (transaction_item ->> 'amount')::numeric(18,2);
            direction_value := lower(transaction_item ->> 'direction');
            narration_value := left(btrim(transaction_item ->> 'narration'), 500);
            external_reference_value := nullif(
                left(btrim(transaction_item ->> 'externalReference'), 120),
                ''
            );
            payment_rail_value := nullif(
                left(upper(btrim(transaction_item ->> 'paymentRail')), 20),
                ''
            );
            transaction_type_value := lower(
                coalesce(transaction_item ->> 'transactionType', 'purchase')
            );
            needs_review_value := coalesce(
                (transaction_item ->> 'needsReview')::boolean,
                false
            );
            occurrence_index_value := coalesce(
                (transaction_item ->> 'occurrenceIndex')::integer,
                1
            );
        EXCEPTION WHEN OTHERS THEN
            RAISE EXCEPTION 'Transaction row % is malformed', received_count_value
                USING ERRCODE = '22023';
        END;

        IF amount_value <= 0
           OR direction_value NOT IN ('debit', 'credit')
           OR narration_value IS NULL
           OR narration_value = ''
           OR occurrence_index_value < 1
           OR occurrence_index_value > 5000
           OR transaction_type_value NOT IN (
               'purchase', 'income', 'transfer', 'fee',
               'refund', 'cash', 'adjustment'
           ) THEN
            RAISE EXCEPTION 'Transaction row % contains invalid values', received_count_value
                USING ERRCODE = '22023';
        END IF;

        transaction_checksum := encode(
            digest(transaction_item::text, 'sha256'),
            'hex'
        );

        INSERT INTO public.raw_transactions (
            user_id,
            import_run_id,
            provider_name,
            external_transaction_ref,
            payload,
            payload_checksum
        )
        VALUES (
            current_user_id,
            import_id_value,
            'statement_import',
            external_reference_value,
            transaction_item,
            transaction_checksum
        )
        ON CONFLICT DO NOTHING
        RETURNING id INTO raw_id_value;

        IF raw_id_value IS NULL THEN
            duplicate_count_value := duplicate_count_value + 1;
            CONTINUE;
        END IF;

        transaction_fingerprint := encode(
            digest(
                account_id_value::text || '|' ||
                occurred_at_value::text || '|' ||
                amount_value::text || '|' ||
                direction_value || '|' ||
                lower(narration_value) || '|' ||
                coalesce(external_reference_value, '') || '|' ||
                occurrence_index_value::text,
                'sha256'
            ),
            'hex'
        );

        INSERT INTO public.financehub_transactions (
            user_id,
            family_id,
            account_id,
            raw_transaction_id,
            external_transaction_ref,
            payment_reference,
            payment_rail,
            narration,
            amount,
            currency,
            direction,
            transaction_type,
            status,
            occurred_at,
            posted_at,
            fingerprint,
            needs_review
        )
        VALUES (
            current_user_id,
            current_family_id,
            account_id_value,
            raw_id_value,
            external_reference_value,
            external_reference_value,
            payment_rail_value,
            narration_value,
            amount_value,
            'INR',
            direction_value,
            transaction_type_value,
            'posted',
            occurred_at_value,
            occurred_at_value,
            transaction_fingerprint,
            needs_review_value
        )
        ON CONFLICT DO NOTHING;

        IF FOUND THEN
            imported_count_value := imported_count_value + 1;
            IF needs_review_value THEN
                review_count_value := review_count_value + 1;
            END IF;
        ELSE
            duplicate_count_value := duplicate_count_value + 1;
        END IF;

        raw_id_value := NULL;
    END LOOP;

    UPDATE public.import_runs
    SET
        status = 'completed',
        received_count = received_count_value,
        imported_count = imported_count_value,
        duplicate_count = duplicate_count_value,
        review_count = review_count_value,
        completed_at = now()
    WHERE id = import_id_value;

    RETURN jsonb_build_object(
        'importId', import_id_value,
        'accountId', account_id_value,
        'status', 'completed',
        'alreadyImported', false,
        'receivedCount', received_count_value,
        'importedCount', imported_count_value,
        'duplicateCount', duplicate_count_value,
        'reviewCount', review_count_value
    );
END
$$;

REVOKE ALL
    ON FUNCTION public.import_financehub_statement(
        TEXT, TEXT, TEXT, TEXT, TEXT, JSONB
    )
    FROM PUBLIC;

GRANT EXECUTE
    ON FUNCTION public.import_financehub_statement(
        TEXT, TEXT, TEXT, TEXT, TEXT, JSONB
    )
    TO authenticated;

COMMENT ON FUNCTION public.import_financehub_statement(
    TEXT, TEXT, TEXT, TEXT, TEXT, JSONB
) IS
    'Atomically imports a user-confirmed statement. Provider connectivity is not implied.';
