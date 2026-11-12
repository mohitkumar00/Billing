-- Seed Demo Data for BillMate Retail POS & Billing System
BEGIN;

-- 1. Update Business details with GSTIN and Legal Name
UPDATE businesses 
SET gstin = '27AAAAA0000A1Z5', 
    legal_name = 'BillMate Retail Solutions LLP',
    pan = 'AAAAA0000A',
    state = 'Maharashtra',
    state_code = '27',
    city = 'Mumbai',
    pincode = '400001',
    phone = '9820012345',
    email = 'shop@billmate.local',
    address_line1 = 'Shop 101, Phoenix Marketcity, Kurla West'
WHERE id = '3ba14d46-72fa-43ff-b422-c286338514c3';

-- 2. Ensure Cashier user exists (PIN: 1234)
INSERT INTO users (id, business_id, role_id, full_name, email, password_hash, pin_hash, is_active, created_at, updated_at)
VALUES (
    '4201f643-de19-4c17-bcd8-637a1919401f',
    '3ba14d46-72fa-43ff-b422-c286338514c3',
    'f06dbd9d-cd14-44f8-9255-ba6d2c69c78f',
    'Ramesh Cashier',
    'cashier@billmate.local',
    '$2a$11$0aF4F0vKzF2m4k6h5y0zWevC9k6l9h4g2k1b7x3c5v8m2l1k0j9i8',
    -- SHA256 / BCrypt pin hash of '1234' matching password hasher
    (SELECT pin_hash FROM users WHERE email = 'admin@billmate.local'),
    true,
    NOW(),
    NOW()
) ON CONFLICT (id) DO UPDATE 
SET pin_hash = (SELECT pin_hash FROM users WHERE email = 'admin@billmate.local');

-- 3. Categories
INSERT INTO categories (id, business_id, name, sort_order, is_deleted, created_at, updated_at)
VALUES 
    ('a0000000-0000-0000-0000-000000000001', '3ba14d46-72fa-43ff-b422-c286338514c3', 'Groceries & Staples', 1, false, NOW(), NOW()),
    ('a0000000-0000-0000-0000-000000000002', '3ba14d46-72fa-43ff-b422-c286338514c3', 'Dairy & Bakery', 2, false, NOW(), NOW()),
    ('a0000000-0000-0000-0000-000000000003', '3ba14d46-72fa-43ff-b422-c286338514c3', 'Snacks & Beverages', 3, false, NOW(), NOW()),
    ('a0000000-0000-0000-0000-000000000004', '3ba14d46-72fa-43ff-b422-c286338514c3', 'Personal & Home Care', 4, false, NOW(), NOW()),
    ('a0000000-0000-0000-0000-000000000005', '3ba14d46-72fa-43ff-b422-c286338514c3', 'Electronics & Lighting', 5, false, NOW(), NOW())
ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;

-- 4. Products (12 items across GST 5%, 12%, 18%, 28%)
-- Tax Slabs:
-- 5%:  9f246e81-a1a2-4a80-a2af-ea3d922b27a3
-- 12%: 0af1a280-1d2b-414c-b09c-fd27504ea22f
-- 18%: de966963-150e-4888-9449-6e2cddfb88bc
-- 28%: 5878dcdf-1d82-4ca0-a668-26ff5f5b2437

INSERT INTO products (
    id, business_id, category_id, tax_slab_id, sku, barcode, name, description, brand,
    unit, is_service, is_tax_inclusive, is_rcm_applicable, purchase_price, selling_price, mrp,
    current_stock, low_stock_threshold, track_batches, is_favourite, is_active, row_version,
    is_deleted, created_at, updated_at
) VALUES
(
    'b0000000-0000-0000-0000-000000000001', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000001', '9f246e81-a1a2-4a80-a2af-ea3d922b27a3',
    'GRO-ATT-001', '8901030383701', 'Aashirvaad Shudh Chakki Atta 5kg', '100% whole wheat flour with natural dietary fibres', 'Aashirvaad',
    'PKT', false, true, false, 210.00, 250.00, 275.00, 45.000, 10.000, false, true, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000002', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000001', '9f246e81-a1a2-4a80-a2af-ea3d922b27a3',
    'GRO-OIL-002', '8906007280145', 'Fortune Sunlite Sunflower Oil 1L', 'Refined edible sunflower cooking oil with Vitamin A & D', 'Fortune',
    'BTL', false, true, false, 120.00, 145.00, 160.00, 60.000, 15.000, false, true, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000003', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000002', '0af1a280-1d2b-414c-b09c-fd27504ea22f',
    'DAI-BUT-003', '8901262010053', 'Amul Pasteurized Butter 500g', 'Pure dairy salted table butter', 'Amul',
    'PKT', false, true, false, 230.00, 275.00, 285.00, 25.000, 8.000, false, true, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000004', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000003', 'de966963-150e-4888-9449-6e2cddfb88bc',
    'SNA-PAR-004', '8901719101037', 'Parle-G Gold Biscuits 1kg', 'Crispy glucose enriched wheat biscuits family pack', 'Parle',
    'PKT', false, true, false, 90.00, 115.00, 130.00, 80.000, 20.000, false, true, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000005', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000001', '9f246e81-a1a2-4a80-a2af-ea3d922b27a3',
    'BEV-TEA-005', '8901052000518', 'Tata Tea Gold 500g', 'Premium blend of CTC and orthodox tea leaves', 'Tata Tea',
    'PKT', false, true, false, 240.00, 290.00, 320.00, 35.000, 10.000, false, false, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000006', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000003', 'de966963-150e-4888-9449-6e2cddfb88bc',
    'SNA-MAG-006', '8901058852449', 'Maggi 2-Minute Masala Noodles 4-Pack', 'Instant noodles with masala tastemaker seasoning', 'Nestle',
    'PKT', false, true, false, 48.00, 56.00, 60.00, 120.000, 25.000, false, true, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000007', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000003', 'de966963-150e-4888-9449-6e2cddfb88bc',
    'SNA-CAD-007', '7622201441227', 'Cadbury Dairy Milk Silk 150g', 'Smooth and creamy chocolate bar', 'Cadbury',
    'BAR', false, true, false, 140.00, 175.00, 180.00, 40.000, 12.000, false, false, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000008', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000003', '5878dcdf-1d82-4ca0-a668-26ff5f5b2437',
    'BEV-COK-008', '8901764012234', 'Coca-Cola Can 300ml', 'Sparkling carbonated cola beverage', 'Coca-Cola',
    'CAN', false, true, false, 30.00, 40.00, 40.00, 50.000, 15.000, false, false, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000009', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000004', 'de966963-150e-4888-9449-6e2cddfb88bc',
    'PER-DET-009', '8901396316129', 'Dettol Original Liquid Handwash 200ml', 'Antibacterial germ protection hand soap pump', 'Dettol',
    'BTL', false, true, false, 75.00, 99.00, 105.00, 18.000, 8.000, false, false, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000010', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000005', 'de966963-150e-4888-9449-6e2cddfb88bc',
    'ELE-EAR-010', '8904329001248', 'boAt BassHeads 100 Wired Earphones', 'In-ear wired earphones with 10mm dynamic drivers', 'boAt',
    'PCS', false, true, false, 280.00, 399.00, 999.00, 4.000, 10.000, false, true, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000011', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000005', 'de966963-150e-4888-9449-6e2cddfb88bc',
    'ELE-BUL-011', '8904218002011', 'Syska 9W LED Bulb B22 Cool Day', 'Energy efficient 6500K cool daylight bulb', 'Syska',
    'PCS', false, true, false, 65.00, 95.00, 140.00, 3.000, 10.000, false, false, true, 1, false, NOW(), NOW()
),
(
    'b0000000-0000-0000-0000-000000000012', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'a0000000-0000-0000-0000-000000000004', 'de966963-150e-4888-9449-6e2cddfb88bc',
    'HOM-SRF-012', '8901030005238', 'Surf Excel Easy Wash Detergent 1kg', 'Advanced stain removal washing powder', 'Surf Excel',
    'PKT', false, true, false, 110.00, 135.00, 145.00, 32.000, 10.000, false, false, true, 1, false, NOW(), NOW()
)
ON CONFLICT (id) DO UPDATE 
SET selling_price = EXCLUDED.selling_price, current_stock = EXCLUDED.current_stock;

-- 5. Customers
INSERT INTO customers (
    id, business_id, name, phone, email, gstin, billing_address, state, state_code,
    pricing_tier, credit_limit, outstanding_balance, loyalty_points, is_deleted, created_at, updated_at
) VALUES
(
    'c0000000-0000-0000-0000-000000000001', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'Walk-in Retail Customer', '9999999999', 'walkin@billmate.local', NULL,
    'Counter Sale, Mumbai', 'Maharashtra', '27',
    'Retail', 0.00, 0.00, 0, false, NOW(), NOW()
),
(
    'c0000000-0000-0000-0000-000000000002', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'Sharma Kirana & General Store', '9820112233', 'sharma.kirana@gmail.com', '27AABCS1429B1ZB',
    'Shop 4, Market Yard, Pune', 'Maharashtra', '27',
    'Wholesale', 50000.00, 0.00, 120, false, NOW(), NOW()
),
(
    'c0000000-0000-0000-0000-000000000003', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'Pooja Supermarket Pvt Ltd', '9833445566', 'purchase@poojasuper.com', '27AAACP0123A1Z8',
    'Plot 12, MIDC Andheri East, Mumbai', 'Maharashtra', '27',
    'Wholesale', 100000.00, 0.00, 450, false, NOW(), NOW()
),
(
    'c0000000-0000-0000-0000-000000000004', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'Delhi Tech Solutions', '9811002233', 'accounts@delhitech.in', '07AAACD4567M1Z1',
    'Flat 304, Barakhamba Road, Connaught Place, New Delhi', 'Delhi', '07',
    'Retail', 25000.00, 494.00, 50, false, NOW(), NOW()
)
ON CONFLICT (id) DO UPDATE 
SET outstanding_balance = EXCLUDED.outstanding_balance;

-- 6. Invoices
-- Financial Year for 2026-11 is '2026-2027'
-- Invoices today (4 invoices) + yesterday (1 invoice)

INSERT INTO invoices (
    id, business_id, customer_id, created_by_user_id, idempotency_key, invoice_number, financial_year,
    invoice_type, status, invoice_date, place_of_supply_code, is_inter_state, is_composition_scheme,
    is_reverse_charge, subtotal, discount_total, taxable_amount, cgst_total, sgst_total, igst_total,
    cess_total, round_off, grand_total, amount_paid, row_version, is_deleted, created_at, updated_at
) VALUES
(
    'd0000000-0000-0000-0000-000000000001', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'c0000000-0000-0000-0000-000000000001', '4201f643-de19-4c17-bcd8-637a1919401e',
    'e0000000-0000-0000-0000-000000000001', 'INV-2627-0001', '2026-2027',
    'TaxInvoice', 'Paid', NOW(), '27', false, false, false,
    362.00, 0.00, 333.02, 14.49, 14.49, 0.00, 0.00, 0.00, 362.00, 362.00, 1, false, NOW(), NOW()
),
(
    'd0000000-0000-0000-0000-000000000002', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'c0000000-0000-0000-0000-000000000002', '4201f643-de19-4c17-bcd8-637a1919401e',
    'e0000000-0000-0000-0000-000000000002', 'INV-2627-0002', '2026-2027',
    'TaxInvoice', 'Paid', NOW(), '27', false, false, false,
    1145.00, 0.00, 1074.12, 35.44, 35.44, 0.00, 0.00, 0.00, 1145.00, 1145.00, 1, false, NOW(), NOW()
),
(
    'd0000000-0000-0000-0000-000000000003', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'c0000000-0000-0000-0000-000000000003', '4201f643-de19-4c17-bcd8-637a1919401e',
    'e0000000-0000-0000-0000-000000000003', 'INV-2627-0003', '2026-2027',
    'TaxInvoice', 'Paid', NOW(), '27', false, false, false,
    930.00, 0.00, 780.20, 74.90, 74.90, 0.00, 0.00, 0.00, 930.00, 930.00, 1, false, NOW(), NOW()
),
(
    'd0000000-0000-0000-0000-000000000004', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'c0000000-0000-0000-0000-000000000004', '4201f643-de19-4c17-bcd8-637a1919401e',
    'e0000000-0000-0000-0000-000000000004', 'INV-2627-0004', '2026-2027',
    'TaxInvoice', 'Partial', NOW(), '07', true, false, false,
    988.00, 0.00, 837.29, 0.00, 0.00, 150.71, 0.00, 0.00, 988.00, 494.00, 1, false, NOW(), NOW()
),
(
    'd0000000-0000-0000-0000-000000000005', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'c0000000-0000-0000-0000-000000000001', '4201f643-de19-4c17-bcd8-637a1919401e',
    'e0000000-0000-0000-0000-000000000005', 'INV-2627-0005', '2026-2027',
    'TaxInvoice', 'Paid', NOW() - INTERVAL '1 day', '27', false, false, false,
    234.00, 0.00, 198.31, 17.84, 17.84, 0.00, 0.00, 0.01, 234.00, 234.00, 1, false, NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day'
)
ON CONFLICT (id) DO NOTHING;

-- 7. Invoice Items
INSERT INTO invoice_items (
    id, invoice_id, product_id, business_id, item_name, unit, quantity, unit_price,
    discount_amount, taxable_amount, cgst_rate, cgst_amount, sgst_rate, sgst_amount,
    igst_rate, igst_amount, cess_rate, cess_amount, line_total, is_deleted, created_at, updated_at
) VALUES
-- Items for INV-2627-0001
(
    'f0000000-0000-0000-0000-000000000001', 'd0000000-0000-0000-0000-000000000001', 'b0000000-0000-0000-0000-000000000001',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Aashirvaad Shudh Chakki Atta 5kg', 'PKT', 1.000, 250.00,
    0.00, 238.10, 2.50, 5.95, 2.50, 5.95, 0.00, 0.00, 0.00, 0.00, 250.00, false, NOW(), NOW()
),
(
    'f0000000-0000-0000-0000-000000000002', 'd0000000-0000-0000-0000-000000000001', 'b0000000-0000-0000-0000-000000000006',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Maggi 2-Minute Masala Noodles 4-Pack', 'PKT', 2.000, 56.00,
    0.00, 94.92, 9.00, 8.54, 9.00, 8.54, 0.00, 0.00, 0.00, 0.00, 112.00, false, NOW(), NOW()
),
-- Items for INV-2627-0002
(
    'f0000000-0000-0000-0000-000000000003', 'd0000000-0000-0000-0000-000000000002', 'b0000000-0000-0000-0000-000000000002',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Fortune Sunlite Sunflower Oil 1L', 'BTL', 2.000, 145.00,
    0.00, 276.19, 2.50, 6.90, 2.50, 6.90, 0.00, 0.00, 0.00, 0.00, 290.00, false, NOW(), NOW()
),
(
    'f0000000-0000-0000-0000-000000000004', 'd0000000-0000-0000-0000-000000000002', 'b0000000-0000-0000-0000-000000000003',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Amul Pasteurized Butter 500g', 'PKT', 1.000, 275.00,
    0.00, 245.54, 6.00, 14.73, 6.00, 14.73, 0.00, 0.00, 0.00, 0.00, 275.00, false, NOW(), NOW()
),
(
    'f0000000-0000-0000-0000-000000000005', 'd0000000-0000-0000-0000-000000000002', 'b0000000-0000-0000-0000-000000000005',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Tata Tea Gold 500g', 'PKT', 2.000, 290.00,
    0.00, 552.38, 2.50, 13.81, 2.50, 13.81, 0.00, 0.00, 0.00, 0.00, 580.00, false, NOW(), NOW()
),
-- Items for INV-2627-0003
(
    'f0000000-0000-0000-0000-000000000006', 'd0000000-0000-0000-0000-000000000003', 'b0000000-0000-0000-0000-000000000004',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Parle-G Gold Biscuits 1kg', 'PKT', 4.000, 115.00,
    0.00, 389.83, 9.00, 35.08, 9.00, 35.08, 0.00, 0.00, 0.00, 0.00, 460.00, false, NOW(), NOW()
),
(
    'f0000000-0000-0000-0000-000000000007', 'd0000000-0000-0000-0000-000000000003', 'b0000000-0000-0000-0000-000000000007',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Cadbury Dairy Milk Silk 150g', 'BAR', 2.000, 175.00,
    0.00, 296.61, 9.00, 26.70, 9.00, 26.70, 0.00, 0.00, 0.00, 0.00, 350.00, false, NOW(), NOW()
),
(
    'f0000000-0000-0000-0000-000000000008', 'd0000000-0000-0000-0000-000000000003', 'b0000000-0000-0000-0000-000000000008',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Coca-Cola Can 300ml', 'CAN', 3.000, 40.00,
    0.00, 93.75, 14.00, 13.12, 14.00, 13.12, 0.00, 0.00, 0.00, 0.00, 120.00, false, NOW(), NOW()
),
-- Items for INV-2627-0004 (Inter-state IGST)
(
    'f0000000-0000-0000-0000-000000000009', 'd0000000-0000-0000-0000-000000000004', 'b0000000-0000-0000-0000-000000000010',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'boAt BassHeads 100 Wired Earphones', 'PCS', 2.000, 399.00,
    0.00, 676.27, 0.00, 0.00, 0.00, 0.00, 18.00, 121.73, 0.00, 0.00, 798.00, false, NOW(), NOW()
),
(
    'f0000000-0000-0000-0000-000000000010', 'd0000000-0000-0000-0000-000000000004', 'b0000000-0000-0000-0000-000000000011',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Syska 9W LED Bulb B22 Cool Day', 'PCS', 2.000, 95.00,
    0.00, 161.02, 0.00, 0.00, 0.00, 0.00, 18.00, 28.98, 0.00, 0.00, 190.00, false, NOW(), NOW()
),
-- Items for INV-2627-0005
(
    'f0000000-0000-0000-0000-000000000011', 'd0000000-0000-0000-0000-000000000005', 'b0000000-0000-0000-0000-000000000012',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Surf Excel Easy Wash Detergent 1kg', 'PKT', 1.000, 135.00,
    0.00, 114.41, 9.00, 10.30, 9.00, 10.30, 0.00, 0.00, 0.00, 0.00, 135.00, false, NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day'
),
(
    'f0000000-0000-0000-0000-000000000012', 'd0000000-0000-0000-0000-000000000005', 'b0000000-0000-0000-0000-000000000009',
    '3ba14d46-72fa-43ff-b422-c286338514c3', 'Dettol Original Liquid Handwash 200ml', 'BTL', 1.000, 99.00,
    0.00, 83.90, 9.00, 7.55, 9.00, 7.55, 0.00, 0.00, 0.00, 0.00, 99.00, false, NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day'
)
ON CONFLICT (id) DO NOTHING;

-- 8. Payments
INSERT INTO payments (
    id, business_id, invoice_id, customer_id, received_by_user_id, payment_mode,
    amount, reference_number, payment_date, is_deleted, created_at, updated_at
) VALUES
(
    '90000000-0000-0000-0000-000000000001', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'd0000000-0000-0000-0000-000000000001', 'c0000000-0000-0000-0000-000000000001',
    '4201f643-de19-4c17-bcd8-637a1919401e', 'Cash', 362.00, 'CASH-REC-001', NOW(), false, NOW(), NOW()
),
(
    '90000000-0000-0000-0000-000000000002', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'd0000000-0000-0000-0000-000000000002', 'c0000000-0000-0000-0000-000000000002',
    '4201f643-de19-4c17-bcd8-637a1919401e', 'Upi', 1145.00, 'UPI-REF-98471203', NOW(), false, NOW(), NOW()
),
(
    '90000000-0000-0000-0000-000000000003', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'd0000000-0000-0000-0000-000000000003', 'c0000000-0000-0000-0000-000000000003',
    '4201f643-de19-4c17-bcd8-637a1919401e', 'Card', 930.00, 'POS-TXN-442819', NOW(), false, NOW(), NOW()
),
(
    '90000000-0000-0000-0000-000000000004', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'd0000000-0000-0000-0000-000000000004', 'c0000000-0000-0000-0000-000000000004',
    '4201f643-de19-4c17-bcd8-637a1919401e', 'BankTransfer', 494.00, 'NEFT-DEL-20261111', NOW(), false, NOW(), NOW()
),
(
    '90000000-0000-0000-0000-000000000005', '3ba14d46-72fa-43ff-b422-c286338514c3',
    'd0000000-0000-0000-0000-000000000005', 'c0000000-0000-0000-0000-000000000001',
    '4201f643-de19-4c17-bcd8-637a1919401e', 'Cash', 234.00, 'CASH-REC-000', NOW() - INTERVAL '1 day', false, NOW() - INTERVAL '1 day', NOW() - INTERVAL '1 day'
)
ON CONFLICT (id) DO NOTHING;

COMMIT;
