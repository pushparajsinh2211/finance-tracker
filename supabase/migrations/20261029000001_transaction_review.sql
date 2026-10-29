-- Unified FinanceHub transaction review, correction audit and import history.
-- Automatic bank/card/UPI provider connectivity remains unconfigured.

CREATE TABLE public.financehub_transaction_audit (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    transaction_id UUID NOT NULL REFERENCES public.financehub_transactions(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,
    previous_category_id UUID REFERENCES public.categories(id) ON DELETE SET NULL,
    new_category_id UUID REFERENCES public.categories(id) ON DELETE SET NULL,
    previous_needs_review BOOLEAN NOT NULL,
    new_needs_review BOOLEAN NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ix_financehub_transaction_audit_user_changed
    ON public.financehub_transaction_audit (user_id, changed_at DESC);

ALTER TABLE public.financehub_transaction_audit ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Users read own FinanceHub transaction audit"
    ON public.financehub_transaction_audit
    FOR SELECT TO authenticated
    USING (user_id = auth.uid());

CREATE OR REPLACE FUNCTION public.list_financehub_transactions(
    p_page INTEGER DEFAULT 1,
    p_page_size INTEGER DEFAULT 25,
    p_search TEXT DEFAULT NULL,
    p_account_id UUID DEFAULT NULL,
    p_category_id UUID DEFAULT NULL,
    p_payment_rail TEXT DEFAULT NULL,
    p_direction TEXT DEFAULT NULL,
    p_needs_review BOOLEAN DEFAULT NULL,
    p_from_date DATE DEFAULT NULL,
    p_to_date DATE DEFAULT NULL
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, extensions
AS $function$
DECLARE
    current_user_id UUID := auth.uid();
    page_value INTEGER := greatest(coalesce(p_page, 1), 1);
    page_size_value INTEGER := least(greatest(coalesce(p_page_size, 25), 1), 100);
    offset_value INTEGER;
    total_count_value BIGINT;
    review_count_value BIGINT;
    items_value JSONB;
    accounts_value JSONB;
    rails_value JSONB;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication is required' USING ERRCODE = '42501';
    END IF;

    IF p_direction IS NOT NULL AND p_direction NOT IN ('debit', 'credit') THEN
        RAISE EXCEPTION 'Direction must be debit or credit' USING ERRCODE = '22023';
    END IF;

    IF p_from_date IS NOT NULL AND p_to_date IS NOT NULL AND p_from_date > p_to_date THEN
        RAISE EXCEPTION 'From date cannot be after to date' USING ERRCODE = '22023';
    END IF;

    offset_value := (page_value - 1) * page_size_value;

    SELECT count(*)
    INTO total_count_value
    FROM public.financehub_transactions transaction_row
    WHERE transaction_row.user_id = current_user_id
      AND transaction_row.deleted_at IS NULL
      AND (p_search IS NULL OR btrim(p_search) = ''
           OR transaction_row.narration ILIKE '%' || btrim(p_search) || '%'
           OR coalesce(transaction_row.merchant_name, '') ILIKE '%' || btrim(p_search) || '%'
           OR coalesce(transaction_row.external_transaction_ref, '') ILIKE '%' || btrim(p_search) || '%')
      AND (p_account_id IS NULL OR transaction_row.account_id = p_account_id)
      AND (p_category_id IS NULL OR transaction_row.category_id = p_category_id)
      AND (p_payment_rail IS NULL OR p_payment_rail = ''
           OR transaction_row.payment_rail = upper(p_payment_rail))
      AND (p_direction IS NULL OR p_direction = ''
           OR transaction_row.direction = p_direction)
      AND (p_needs_review IS NULL OR transaction_row.needs_review = p_needs_review)
      AND (p_from_date IS NULL OR transaction_row.occurred_at >= (p_from_date::timestamp AT TIME ZONE 'Asia/Kolkata'))
      AND (p_to_date IS NULL OR transaction_row.occurred_at < ((p_to_date + 1)::timestamp AT TIME ZONE 'Asia/Kolkata'));

    SELECT count(*)
    INTO review_count_value
    FROM public.financehub_transactions transaction_row
    WHERE transaction_row.user_id = current_user_id
      AND transaction_row.deleted_at IS NULL
      AND transaction_row.needs_review;

    SELECT coalesce(jsonb_agg(item ORDER BY item_occurred_at DESC), '[]'::jsonb)
    INTO items_value
    FROM (
        SELECT
            transaction_row.occurred_at AS item_occurred_at,
            jsonb_build_object(
                'id', transaction_row.id,
                'occurredAt', transaction_row.occurred_at,
                'postedAt', transaction_row.posted_at,
                'accountId', transaction_row.account_id,
                'accountName', account_row.display_name,
                'sourceType', account_row.source_type,
                'maskedIdentifier', account_row.masked_identifier,
                'narration', transaction_row.narration,
                'merchantName', transaction_row.merchant_name,
                'amount', transaction_row.amount,
                'currency', transaction_row.currency,
                'direction', transaction_row.direction,
                'transactionType', transaction_row.transaction_type,
                'status', transaction_row.status,
                'paymentRail', transaction_row.payment_rail,
                'externalReference', transaction_row.external_transaction_ref,
                'categoryId', transaction_row.category_id,
                'categoryName', category_row.name,
                'categoryColor', category_row.color,
                'needsReview', transaction_row.needs_review,
                'categorySource', transaction_row.category_source,
                'importRunId', raw_row.import_run_id
            ) AS item
        FROM public.financehub_transactions transaction_row
        JOIN public.financial_accounts account_row
          ON account_row.id = transaction_row.account_id
        LEFT JOIN public.categories category_row
          ON category_row.id = transaction_row.category_id
        LEFT JOIN public.raw_transactions raw_row
          ON raw_row.id = transaction_row.raw_transaction_id
        WHERE transaction_row.user_id = current_user_id
          AND transaction_row.deleted_at IS NULL
          AND (p_search IS NULL OR btrim(p_search) = ''
               OR transaction_row.narration ILIKE '%' || btrim(p_search) || '%'
               OR coalesce(transaction_row.merchant_name, '') ILIKE '%' || btrim(p_search) || '%'
               OR coalesce(transaction_row.external_transaction_ref, '') ILIKE '%' || btrim(p_search) || '%')
          AND (p_account_id IS NULL OR transaction_row.account_id = p_account_id)
          AND (p_category_id IS NULL OR transaction_row.category_id = p_category_id)
          AND (p_payment_rail IS NULL OR p_payment_rail = ''
               OR transaction_row.payment_rail = upper(p_payment_rail))
          AND (p_direction IS NULL OR p_direction = ''
               OR transaction_row.direction = p_direction)
          AND (p_needs_review IS NULL OR transaction_row.needs_review = p_needs_review)
          AND (p_from_date IS NULL OR transaction_row.occurred_at >= (p_from_date::timestamp AT TIME ZONE 'Asia/Kolkata'))
          AND (p_to_date IS NULL OR transaction_row.occurred_at < ((p_to_date + 1)::timestamp AT TIME ZONE 'Asia/Kolkata'))
        ORDER BY transaction_row.occurred_at DESC, transaction_row.id
        LIMIT page_size_value
        OFFSET offset_value
    ) page_rows;

    SELECT coalesce(
        jsonb_agg(
            jsonb_build_object(
                'id', account_row.id,
                'name', account_row.display_name,
                'sourceType', account_row.source_type,
                'maskedIdentifier', account_row.masked_identifier
            )
            ORDER BY account_row.display_name
        ),
        '[]'::jsonb
    )
    INTO accounts_value
    FROM public.financial_accounts account_row
    WHERE account_row.user_id = current_user_id
      AND account_row.status = 'active';

    SELECT coalesce(jsonb_agg(rail ORDER BY rail), '[]'::jsonb)
    INTO rails_value
    FROM (
        SELECT DISTINCT transaction_row.payment_rail AS rail
        FROM public.financehub_transactions transaction_row
        WHERE transaction_row.user_id = current_user_id
          AND transaction_row.deleted_at IS NULL
          AND transaction_row.payment_rail IS NOT NULL
    ) payment_rails;

    RETURN jsonb_build_object(
        'items', items_value,
        'page', page_value,
        'pageSize', page_size_value,
        'totalCount', total_count_value,
        'totalPages', CASE
            WHEN total_count_value = 0 THEN 0
            ELSE ceil(total_count_value::numeric / page_size_value)::integer
        END,
        'reviewCount', review_count_value,
        'accounts', accounts_value,
        'paymentRails', rails_value
    );
END
$function$;

CREATE OR REPLACE FUNCTION public.review_financehub_transaction(
    p_transaction_id UUID,
    p_category_id UUID DEFAULT NULL,
    p_needs_review BOOLEAN DEFAULT false
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, extensions
AS $function$
DECLARE
    current_user_id UUID := auth.uid();
    transaction_row public.financehub_transactions%ROWTYPE;
    category_row public.categories%ROWTYPE;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication is required' USING ERRCODE = '42501';
    END IF;

    SELECT *
    INTO transaction_row
    FROM public.financehub_transactions
    WHERE id = p_transaction_id
      AND user_id = current_user_id
      AND deleted_at IS NULL
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Transaction not found' USING ERRCODE = '42501';
    END IF;

    IF p_category_id IS NOT NULL THEN
        SELECT *
        INTO category_row
        FROM public.categories
        WHERE id = p_category_id
          AND family_id = transaction_row.family_id
          AND NOT is_archived;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Category is unavailable for this family'
                USING ERRCODE = '22023';
        END IF;
    END IF;

    INSERT INTO public.financehub_transaction_audit (
        transaction_id,
        user_id,
        previous_category_id,
        new_category_id,
        previous_needs_review,
        new_needs_review
    )
    VALUES (
        transaction_row.id,
        current_user_id,
        transaction_row.category_id,
        p_category_id,
        transaction_row.needs_review,
        coalesce(p_needs_review, false)
    );

    UPDATE public.financehub_transactions
    SET
        category_id = p_category_id,
        category_source = CASE WHEN p_category_id IS NULL THEN NULL ELSE 'user' END,
        category_confidence = CASE WHEN p_category_id IS NULL THEN NULL ELSE 1 END,
        needs_review = coalesce(p_needs_review, false),
        updated_at = now()
    WHERE id = transaction_row.id;

    RETURN (
        SELECT jsonb_build_object(
            'id', updated_row.id,
            'categoryId', updated_row.category_id,
            'categoryName', updated_category.name,
            'categoryColor', updated_category.color,
            'categorySource', updated_row.category_source,
            'needsReview', updated_row.needs_review,
            'updatedAt', updated_row.updated_at
        )
        FROM public.financehub_transactions updated_row
        LEFT JOIN public.categories updated_category
          ON updated_category.id = updated_row.category_id
        WHERE updated_row.id = transaction_row.id
    );
END
$function$;

CREATE OR REPLACE FUNCTION public.list_financehub_imports(
    p_page INTEGER DEFAULT 1,
    p_page_size INTEGER DEFAULT 10
)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, extensions
AS $function$
DECLARE
    current_user_id UUID := auth.uid();
    page_value INTEGER := greatest(coalesce(p_page, 1), 1);
    page_size_value INTEGER := least(greatest(coalesce(p_page_size, 10), 1), 50);
    total_count_value BIGINT;
    items_value JSONB;
BEGIN
    IF current_user_id IS NULL THEN
        RAISE EXCEPTION 'Authentication is required' USING ERRCODE = '42501';
    END IF;

    SELECT count(*)
    INTO total_count_value
    FROM public.import_runs import_row
    WHERE import_row.user_id = current_user_id;

    SELECT coalesce(jsonb_agg(item ORDER BY item_created_at DESC), '[]'::jsonb)
    INTO items_value
    FROM (
        SELECT
            import_row.created_at AS item_created_at,
            jsonb_build_object(
                'id', import_row.id,
                'sourceKind', import_row.source_kind,
                'status', import_row.status,
                'receivedCount', import_row.received_count,
                'importedCount', import_row.imported_count,
                'duplicateCount', import_row.duplicate_count,
                'reviewCount', import_row.review_count,
                'startedAt', import_row.started_at,
                'completedAt', import_row.completed_at,
                'createdAt', import_row.created_at,
                'accountName', account_lookup.display_name,
                'sourceType', account_lookup.source_type
            ) AS item
        FROM public.import_runs import_row
        LEFT JOIN LATERAL (
            SELECT account_row.display_name, account_row.source_type
            FROM public.raw_transactions raw_row
            JOIN public.financehub_transactions transaction_row
              ON transaction_row.raw_transaction_id = raw_row.id
            JOIN public.financial_accounts account_row
              ON account_row.id = transaction_row.account_id
            WHERE raw_row.import_run_id = import_row.id
            ORDER BY transaction_row.created_at
            LIMIT 1
        ) account_lookup ON true
        WHERE import_row.user_id = current_user_id
        ORDER BY import_row.created_at DESC
        LIMIT page_size_value
        OFFSET (page_value - 1) * page_size_value
    ) page_rows;

    RETURN jsonb_build_object(
        'items', items_value,
        'page', page_value,
        'pageSize', page_size_value,
        'totalCount', total_count_value,
        'totalPages', CASE
            WHEN total_count_value = 0 THEN 0
            ELSE ceil(total_count_value::numeric / page_size_value)::integer
        END
    );
END
$function$;

REVOKE ALL ON FUNCTION public.list_financehub_transactions(
    INTEGER, INTEGER, TEXT, UUID, UUID, TEXT, TEXT, BOOLEAN, DATE, DATE
) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.review_financehub_transaction(
    UUID, UUID, BOOLEAN
) FROM PUBLIC;
REVOKE ALL ON FUNCTION public.list_financehub_imports(
    INTEGER, INTEGER
) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION public.list_financehub_transactions(
    INTEGER, INTEGER, TEXT, UUID, UUID, TEXT, TEXT, BOOLEAN, DATE, DATE
) TO authenticated;
GRANT EXECUTE ON FUNCTION public.review_financehub_transaction(
    UUID, UUID, BOOLEAN
) TO authenticated;
GRANT EXECUTE ON FUNCTION public.list_financehub_imports(
    INTEGER, INTEGER
) TO authenticated;

COMMENT ON FUNCTION public.review_financehub_transaction(UUID, UUID, BOOLEAN) IS
    'Applies a user review decision to an owned imported transaction and records an immutable audit row.';
