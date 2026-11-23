-- BillMate idempotent demo seed
-- Run with: psql -h localhost -U postgres -d billmatedb -f scripts/seed-demo-data-idempotent.sql

DO $$
DECLARE
	v_business_id uuid;
	v_admin_user_id uuid;
	v_admin_password_hash text;
	v_admin_pin_hash text;

	v_admin_role_id uuid;
	v_cashier_role_id uuid;
	v_manager_role_id uuid;
	v_accountant_role_id uuid;

	v_customer_walkin_id uuid := '11111111-1111-1111-1111-111111111101';
	v_customer_arya_id   uuid := '11111111-1111-1111-1111-111111111102';

	v_tax18_id uuid;
	v_tax5_id uuid;

	v_product_rice_id  uuid := '22222222-2222-2222-2222-222222222201';
	v_product_soap_id  uuid := '22222222-2222-2222-2222-222222222202';
	v_product_phone_id uuid := '22222222-2222-2222-2222-222222222203';

	v_invoice_001_id uuid := '33333333-3333-3333-3333-333333333301';
	v_invoice_002_id uuid := '33333333-3333-3333-3333-333333333302';
	v_invoice_001_idem_key uuid := '33333333-3333-3333-3333-333333333901';
	v_invoice_002_idem_key uuid := '33333333-3333-3333-3333-333333333902';

	v_exp_cat_rent_id uuid;
	v_exp_cat_util_id uuid;

	v_admin_owner_id uuid := '4201f643-de19-4c17-bcd8-637a1919401e';
	v_cashier_user_id uuid := '4201f643-de19-4c17-bcd8-637a1919401f';
	v_manager_user_id uuid := '4201f643-de19-4c17-bcd8-637a19194020';
	v_accountant_user_id uuid := '4201f643-de19-4c17-bcd8-637a19194021';
BEGIN
	SELECT id
	INTO v_business_id
	FROM businesses
	WHERE is_deleted = false
	ORDER BY created_at
	LIMIT 1;

	IF v_business_id IS NULL THEN
		RAISE EXCEPTION 'No business found. Start API once so default seed runs first.';
	END IF;

	SELECT id, password_hash, pin_hash
	INTO v_admin_user_id, v_admin_password_hash, v_admin_pin_hash
	FROM users
	WHERE business_id = v_business_id
	  AND email = 'admin@billmate.local'
	  AND is_deleted = false
	LIMIT 1;

	IF v_admin_user_id IS NULL THEN
		RAISE EXCEPTION 'admin@billmate.local not found. Ensure default seed has run.';
	END IF;

	SELECT id INTO v_admin_role_id      FROM roles WHERE business_id = v_business_id AND name = 'ADMIN' AND is_deleted = false LIMIT 1;
	SELECT id INTO v_cashier_role_id    FROM roles WHERE business_id = v_business_id AND name = 'CASHIER' AND is_deleted = false LIMIT 1;
	SELECT id INTO v_manager_role_id    FROM roles WHERE business_id = v_business_id AND name = 'MANAGER' AND is_deleted = false LIMIT 1;
	SELECT id INTO v_accountant_role_id FROM roles WHERE business_id = v_business_id AND name = 'ACCOUNTANT' AND is_deleted = false LIMIT 1;

	IF v_admin_role_id IS NULL OR v_cashier_role_id IS NULL OR v_manager_role_id IS NULL OR v_accountant_role_id IS NULL THEN
		RAISE EXCEPTION 'One or more required roles are missing (ADMIN/CASHIER/MANAGER/ACCOUNTANT).';
	END IF;

	INSERT INTO users (id, business_id, role_id, full_name, email, phone, password_hash, pin_hash, is_active)
	VALUES
		(v_admin_owner_id, v_business_id, v_admin_role_id, 'Shop Administrator', 'owner@billmate.local', '9800000001', v_admin_password_hash, v_admin_pin_hash, true),
		(v_cashier_user_id, v_business_id, v_cashier_role_id, 'Ramesh Cashier', 'cashier@billmate.local', '9800000010', v_admin_password_hash, v_admin_pin_hash, true),
		(v_manager_user_id, v_business_id, v_manager_role_id, 'Maya Manager', 'manager@billmate.local', '9800000011', v_admin_password_hash, v_admin_pin_hash, true),
		(v_accountant_user_id, v_business_id, v_accountant_role_id, 'Arjun Accountant', 'accounts@billmate.local', '9800000012', v_admin_password_hash, v_admin_pin_hash, true)
	ON CONFLICT (business_id, email) DO NOTHING;

	SELECT id INTO v_tax18_id FROM tax_slabs WHERE business_id = v_business_id AND total_rate = 18 AND is_deleted = false LIMIT 1;
	SELECT id INTO v_tax5_id  FROM tax_slabs WHERE business_id = v_business_id AND total_rate = 5  AND is_deleted = false LIMIT 1;

	IF v_tax18_id IS NULL OR v_tax5_id IS NULL THEN
		RAISE EXCEPTION 'Required tax slabs (5%%/18%%) are missing.';
	END IF;

	INSERT INTO customers (id, business_id, name, phone, email, gstin, state, state_code, pricing_tier, credit_limit, outstanding_balance, loyalty_points)
	VALUES
		(v_customer_walkin_id, v_business_id, 'Walk-in Customer', '9990000001', 'walkin@billmate.local', NULL, 'Maharashtra', '27', 'Retail', 0, 0, 0),
		(v_customer_arya_id, v_business_id, 'Arya Traders', '9990000002', 'purchase@aryatraders.in', '27ABCDE1234F1Z5', 'Maharashtra', '27', 'Wholesale', 50000, 0, 0)
	ON CONFLICT (business_id, phone) DO NOTHING;

	INSERT INTO products (id, business_id, tax_slab_id, hsn_sac_code, sku, barcode, name, unit, is_service, is_tax_inclusive, is_rcm_applicable, purchase_price, selling_price, mrp, current_stock, low_stock_threshold, track_batches, is_favourite, is_active, row_version)
	VALUES
		(v_product_rice_id,  v_business_id, v_tax5_id,  '1006', 'RICE-5KG',  '8900000001011', 'Premium Rice 5kg', 'BAG', false, true, false, 220, 250, 260, 50, 10, false, true, true, 1),
		(v_product_soap_id,  v_business_id, v_tax18_id, '3401', 'SOAP-100G', '8900000001028', 'Daily Soap 100g',  'PCS', false, true, false, 18,  25,  28,  200, 25, false, false, true, 1),
		(v_product_phone_id, v_business_id, v_tax18_id, '8517', 'PHONE-USB', '8900000001035', 'USB-C Charger 20W', 'PCS', false, true, false, 450, 699, 799, 40, 5, false, true, true, 1)
	ON CONFLICT (business_id, sku) DO NOTHING;

	INSERT INTO stock_movements (id, business_id, product_id, movement_type, quantity_delta, balance_after, reference_type, reference_id, reason, created_by_user_id)
	VALUES
		('66666666-6666-6666-6666-666666666601', v_business_id, v_product_rice_id,  'Opening', 50,  50,  'SEED', v_product_rice_id,  'Demo opening stock', v_admin_user_id),
		('66666666-6666-6666-6666-666666666602', v_business_id, v_product_soap_id,  'Opening', 200, 200, 'SEED', v_product_soap_id,  'Demo opening stock', v_admin_user_id),
		('66666666-6666-6666-6666-666666666603', v_business_id, v_product_phone_id, 'Opening', 40,  40,  'SEED', v_product_phone_id, 'Demo opening stock', v_admin_user_id)
	ON CONFLICT (id) DO NOTHING;

	INSERT INTO invoices (
		id, business_id, customer_id, created_by_user_id, device_id, idempotency_key,
		invoice_number, financial_year, invoice_type, status, invoice_date,
		place_of_supply_code, is_inter_state, is_composition_scheme, is_reverse_charge,
		subtotal, discount_total, taxable_amount, cgst_total, sgst_total, igst_total, cess_total,
		round_off, grand_total, amount_paid, row_version, notes
	)
	VALUES
		(
			v_invoice_001_id, v_business_id, v_customer_walkin_id, v_cashier_user_id, NULL, v_invoice_001_idem_key,
			'INV-0001', '2026-27', 'TaxInvoice', 'Paid', now() - interval '2 day',
			'27', false, false, false,
			275.00, 0.00, 259.29, 7.86, 7.86, 0.00, 0.00,
			0.00, 275.00, 275.00, 1, 'Seeded demo invoice (cash)'
		),
		(
			v_invoice_002_id, v_business_id, v_customer_arya_id, v_cashier_user_id, NULL, v_invoice_002_idem_key,
			'INV-0002', '2026-27', 'TaxInvoice', 'Partial', now() - interval '1 day',
			'27', false, false, false,
			724.00, 0.00, 613.56, 55.22, 55.22, 0.00, 0.00,
			0.00, 724.00, 500.00, 1, 'Seeded demo invoice (partial UPI payment)'
		)
	ON CONFLICT (business_id, idempotency_key) DO NOTHING;

	INSERT INTO invoice_items (
		id, business_id, invoice_id, product_id, item_name, hsn_sac_code, unit, quantity,
		unit_price, discount_amount, taxable_amount,
		cgst_rate, cgst_amount, sgst_rate, sgst_amount, igst_rate, igst_amount, cess_rate, cess_amount,
		line_total
	)
	VALUES
		('77777777-7777-7777-7777-777777777701', v_business_id, v_invoice_001_id, v_product_rice_id, 'Premium Rice 5kg', '1006', 'BAG', 1, 250.00, 0.00, 238.10, 2.50, 5.95, 2.50, 5.95, 0.00, 0.00, 0.00, 0.00, 250.00),
		('77777777-7777-7777-7777-777777777702', v_business_id, v_invoice_001_id, v_product_soap_id, 'Daily Soap 100g', '3401', 'PCS', 1, 25.00,  0.00, 21.19, 9.00, 1.91, 9.00, 1.91, 0.00, 0.00, 0.00, 0.00, 25.00),
		('77777777-7777-7777-7777-777777777703', v_business_id, v_invoice_002_id, v_product_phone_id, 'USB-C Charger 20W', '8517', 'PCS', 1, 699.00, 0.00, 592.37, 9.00, 53.31, 9.00, 53.31, 0.00, 0.00, 0.00, 0.00, 699.00),
		('77777777-7777-7777-7777-777777777704', v_business_id, v_invoice_002_id, v_product_soap_id, 'Daily Soap 100g', '3401', 'PCS', 1, 25.00,  0.00, 21.19, 9.00, 1.91, 9.00, 1.91, 0.00, 0.00, 0.00, 0.00, 25.00)
	ON CONFLICT (id) DO NOTHING;

	INSERT INTO payments (id, business_id, invoice_id, customer_id, received_by_user_id, payment_mode, amount, reference_number, payment_date, notes)
	VALUES
		('44444444-4444-4444-4444-444444444401', v_business_id, v_invoice_001_id, v_customer_walkin_id, v_cashier_user_id, 'Cash', 275.00, NULL, now() - interval '2 day', 'Full cash payment'),
		('44444444-4444-4444-4444-444444444402', v_business_id, v_invoice_002_id, v_customer_arya_id, v_cashier_user_id, 'Upi', 500.00, 'UPI-SEED-500', now() - interval '1 day', 'Part payment via UPI')
	ON CONFLICT (id) DO NOTHING;

	SELECT id INTO v_exp_cat_rent_id FROM expense_categories WHERE business_id = v_business_id AND name = 'Shop Rent' AND is_deleted = false LIMIT 1;
	SELECT id INTO v_exp_cat_util_id FROM expense_categories WHERE business_id = v_business_id AND name = 'Electricity & Utilities' AND is_deleted = false LIMIT 1;

	IF v_exp_cat_rent_id IS NOT NULL THEN
		INSERT INTO expenses (id, business_id, expense_category_id, supplier_id, created_by_user_id, amount, payment_mode, expense_date, description)
		VALUES ('55555555-5555-5555-5555-555555555501', v_business_id, v_exp_cat_rent_id, NULL, v_manager_user_id, 18000.00, 'BankTransfer', now() - interval '4 day', 'Monthly shop rent (demo)')
		ON CONFLICT (id) DO NOTHING;
	END IF;

	IF v_exp_cat_util_id IS NOT NULL THEN
		INSERT INTO expenses (id, business_id, expense_category_id, supplier_id, created_by_user_id, amount, payment_mode, expense_date, description)
		VALUES ('55555555-5555-5555-5555-555555555502', v_business_id, v_exp_cat_util_id, NULL, v_manager_user_id, 3250.00, 'Cash', now() - interval '3 day', 'Electricity and internet bill (demo)')
		ON CONFLICT (id) DO NOTHING;
	END IF;

	RAISE NOTICE 'Seed complete. business_id=%', v_business_id;
END $$;psql -h localhost -U postgres -d billmatedb -f scripts/seed-demo-data-idempotent.sql