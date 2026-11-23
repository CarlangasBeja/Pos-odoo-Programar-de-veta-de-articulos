-- Carga masiva de datos SINTÉTICOS para pruebas de rendimiento (no son datos de negocio reales).
--
-- Se ejecuta con psql dentro del contenedor de PostgreSQL (lo hace odoo/carga-masiva.ps1):
--   psql -U odoo -d pos -v productos=1000000 -v ordenes=1000000 -f carga_masiva.sql
--
-- Por qué SQL y no el ORM de Odoo: crear un millón de registros con el ORM tarda horas. Aquí se CLONAN
-- registros reales creados por odoo/setup_pos.py (productos y órdenes que Odoo ya validó), cambiando solo
-- nombre, códigos, precio, fechas y uuid. Así los datos tienen exactamente la forma que Odoo espera.
-- Esto es una herramienta de preparación de datos de prueba; la API .NET sigue sin acceder a PostgreSQL.
--
-- Marcas para poder borrar todo después (odoo/tools/borrar_carga_masiva.sql):
--   productos: default_code 'GEN-0000001'...  |  órdenes: sesión 'POS Web .NET/HISTORICO'
--
-- Es repetible: primero borra la carga anterior.
\set ON_ERROR_STOP on
\timing on

\echo '== 0. Borrando una carga anterior (si existe)'
\ir borrar_carga_masiva.sql

BEGIN;
SET LOCAL synchronous_commit = off;

-- ---------------------------------------------------------------------------------------------------
\echo '== 1. Productos'
CREATE TEMP TABLE base_tmpl ON COMMIT DROP AS
SELECT t.id, (row_number() OVER (ORDER BY t.id) - 1)::int AS k
FROM product_template t
WHERE t.default_code ~ '^(CAF|BEB|COM|PAN|SNK)-[0-9]+$' AND t.available_in_pos AND t.active;
SELECT count(*) AS nbase FROM base_tmpl \gset
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM base_tmpl) THEN
    RAISE EXCEPTION 'No hay productos base. Ejecute primero odoo/setup.ps1.';
  END IF;
END $$;

CREATE TEMP TABLE gen ON COMMIT DROP AS
SELECT g,
       (g % :nbase)::int AS k,
       (ARRAY['Café','Té','Jugo','Agua','Galletas','Pan','Torta','Sándwich','Ensalada','Yogur',
              'Queso','Chocolate','Refresco','Empanada','Papas','Arroz','Leche','Avena','Granola','Helado'])[1 + floor(random() * 20)::int]
       || ' ' || (ARRAY['clásico','premium','artesanal','light','orgánico','familiar','mini','extra',
                        'tradicional','especial','integral','casero'])[1 + floor(random() * 12)::int]
       || ' ' || (ARRAY['Andino','del Valle','La Huerta','Don Pepe','Sierra','Costa','El Trigal',
                        'Primavera','Montaña','Ambato','Pichincha','Tungurahua'])[1 + floor(random() * 12)::int]
       || ' ' || g AS nm,
       round((500 + random() * 49500)::numeric, -2) AS price
FROM generate_series(1, :productos) AS g;

INSERT INTO product_template (
    sequence, categ_id, uom_id, company_id, color, base_unit_id, create_uid, write_uid, type, service_tracking,
    barcode, default_code, name, description, description_purchase, description_sale, product_properties,
    list_price, volume, weight, base_unit_count, is_storable, sale_ok, purchase_ok, active,
    can_image_1024_be_zoomed, has_configurable_attributes, is_favorite, create_date, write_date,
    property_account_income_id, property_account_expense_id, pos_sequence, public_description, available_in_pos, to_weight)
SELECT t.sequence, t.categ_id, t.uom_id, t.company_id, t.color, t.base_unit_id, t.create_uid, t.write_uid, t.type, t.service_tracking,
       '8' || lpad(g.g::text, 12, '0'), 'GEN-' || lpad(g.g::text, 7, '0'),
       jsonb_build_object('en_US', g.nm, 'es_419', g.nm), NULL, NULL, NULL, t.product_properties,
       g.price, t.volume, t.weight, t.base_unit_count, t.is_storable, true, t.purchase_ok, true,
       false, false, false, now(), now(),
       t.property_account_income_id, t.property_account_expense_id, t.pos_sequence, NULL, true, t.to_weight
FROM gen g
JOIN base_tmpl b ON b.k = g.k
JOIN product_template t ON t.id = b.id;

CREATE TEMP TABLE new_tmpl ON COMMIT DROP AS
SELECT nt.id, nt.default_code, nt.barcode, nt.list_price, b.id AS base_id
FROM product_template nt
JOIN base_tmpl b ON b.k = (substr(nt.default_code, 5)::int % :nbase)
WHERE nt.default_code LIKE 'GEN-%';

INSERT INTO product_product (
    product_tmpl_id, base_unit_id, create_uid, write_uid, default_code, barcode, combination_indices,
    standard_price, qty_available, lst_price, volume, weight, base_unit_count, active,
    can_image_variant_1024_be_zoomed, is_favorite, write_date, create_date)
SELECT n.id, p.base_unit_id, p.create_uid, p.write_uid, n.default_code, n.barcode, p.combination_indices,
       p.standard_price, p.qty_available, n.list_price, p.volume, p.weight, p.base_unit_count, true,
       false, false, now(), now()
FROM new_tmpl n
JOIN product_product p ON p.product_tmpl_id = n.base_id;

INSERT INTO product_taxes_rel (prod_id, tax_id)
SELECT n.id, r.tax_id FROM new_tmpl n JOIN product_taxes_rel r ON r.prod_id = n.base_id;

INSERT INTO pos_category_product_template_rel (product_template_id, pos_category_id)
SELECT n.id, r.pos_category_id FROM new_tmpl n JOIN pos_category_product_template_rel r ON r.product_template_id = n.base_id;

SELECT count(*) AS productos_creados FROM new_tmpl;

-- ---------------------------------------------------------------------------------------------------
\echo '== 2. Sesión histórica (cerrada) para no cargar la sesión abierta del POS'
INSERT INTO pos_session (config_id, user_id, create_uid, write_uid, name, state, start_at, stop_at, create_date, write_date)
SELECT s.config_id, s.user_id, s.create_uid, s.write_uid, 'POS Web .NET/HISTORICO', 'closed',
       now() - interval '366 days', now() - interval '1 day', now(), now()
FROM pos_session s
WHERE s.config_id = (SELECT config_id FROM pos_session ORDER BY id LIMIT 1)
ORDER BY s.id LIMIT 1
RETURNING id AS hist_session \gset

\echo '== 3. Órdenes históricas'
SELECT id AS tmpl_order FROM pos_order WHERE state IN ('paid', 'done') ORDER BY id LIMIT 1 \gset
SELECT min(p.id) AS minp, count(*) AS np FROM product_product p WHERE p.default_code LIKE 'GEN-%' \gset

INSERT INTO pos_order (
    user_id, company_id, pricelist_id, partner_id, sequence_number, session_id, config_id, preset_id, nb_print,
    sale_journal, fiscal_position_id, create_uid, write_uid, access_token, name, state, pos_reference, uuid,
    source, internal_note, amount_difference, amount_tax, amount_total, amount_paid, amount_return,
    currency_rate, tip_amount, is_refund, to_invoice, is_tipped, has_deleted_line, defer_invoice_pdf,
    date_order, create_date, write_date)
SELECT t.user_id, t.company_id, t.pricelist_id, NULL, g, :hist_session, t.config_id, t.preset_id, 0,
       t.sale_journal, t.fiscal_position_id, t.create_uid, t.write_uid, md5(random()::text),
       'Histórico - ' || lpad(g::text, 7, '0'), 'paid', '900-1-' || lpad(g::text, 7, '0'), gen_random_uuid()::text,
       t.source, 'Venta histórica sintética (prueba de carga).', 0, 0, 0, 0, 0,
       t.currency_rate, 0, false, false, false, false, false,
       d.dt, d.dt, d.dt
FROM generate_series(1, :ordenes) AS g
CROSS JOIN LATERAL (SELECT now() - (random() * interval '365 days') + (g * interval '0 second') AS dt) d
CROSS JOIN (SELECT * FROM pos_order WHERE id = :tmpl_order) t;

\echo '== 4. Líneas (2 por orden, productos sintéticos, IVA 19 %)'
INSERT INTO pos_order_line (
    company_id, product_id, order_id, create_uid, write_uid, name, full_product_name, uuid,
    price_unit, qty, price_subtotal, price_subtotal_incl, discount, total_cost, is_total_cost_computed,
    is_edited, price_type, create_date, write_date)
SELECT o.company_id, pp.id, o.id, o.create_uid, o.write_uid, 'HIST/' || o.id || '/' || n,
       pt.name ->> 'es_419', gen_random_uuid()::text,
       pt.list_price, q.qty, pt.list_price * q.qty, round(pt.list_price * q.qty * 1.19), 0, 0, false,
       false, 'original', o.date_order, o.date_order
FROM pos_order o
CROSS JOIN generate_series(1, 2) AS n
CROSS JOIN LATERAL (SELECT (1 + ((o.id + n) % 4))::numeric AS qty) q
JOIN product_product pp ON pp.id = :minp + ((o.id::bigint * 7919 + n * 104729) % :np)
JOIN product_template pt ON pt.id = pp.product_tmpl_id
WHERE o.session_id = :hist_session;

\echo '== 5. Totales de las órdenes'
UPDATE pos_order o
SET amount_total = s.total, amount_paid = s.total, amount_tax = s.total - s.untaxed
FROM (SELECT order_id, sum(price_subtotal_incl) AS total, sum(price_subtotal) AS untaxed
      FROM pos_order_line l JOIN pos_order po ON po.id = l.order_id
      WHERE po.session_id = :hist_session GROUP BY order_id) s
WHERE o.id = s.order_id;

\echo '== 6. Pagos en efectivo'
SELECT id AS cash_method FROM pos_payment_method WHERE type = 'cash' ORDER BY id LIMIT 1 \gset
INSERT INTO pos_payment (pos_order_id, payment_method_id, session_id, company_id, uuid, amount, is_change,
                         payment_status, payment_date, create_date, write_date, create_uid, write_uid)
SELECT o.id, :cash_method, o.session_id, o.company_id, gen_random_uuid()::text, o.amount_total, false,
       'done', o.date_order, o.date_order, o.date_order, o.create_uid, o.write_uid
FROM pos_order o
WHERE o.session_id = :hist_session;

COMMIT;

\echo '== 7. Estadísticas para el planificador'
ANALYZE product_template;
ANALYZE product_product;
ANALYZE product_taxes_rel;
ANALYZE pos_category_product_template_rel;
ANALYZE pos_order;
ANALYZE pos_order_line;
ANALYZE pos_payment;

SELECT 'productos' AS tabla, count(*) FROM product_product
UNION ALL SELECT 'ordenes', count(*) FROM pos_order
UNION ALL SELECT 'lineas', count(*) FROM pos_order_line
UNION ALL SELECT 'pagos', count(*) FROM pos_payment;
SELECT pg_size_pretty(pg_database_size(current_database())) AS tamano_base;
