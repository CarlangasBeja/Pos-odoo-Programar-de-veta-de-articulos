-- Borra los datos sintéticos creados por carga_masiva.sql (productos GEN-* y la sesión HISTORICO con sus órdenes).
-- No toca los productos ni las ventas reales creados por odoo/setup_pos.py o por el POS.
-- Si un producto sintético se usó en una venta REAL, Odoo no permite borrarlo (la línea de venta lo referencia):
-- ese producto se archiva (inactivo, fuera del POS) y su código cambia a GENV-... para no chocar con una carga nueva.
-- Las categorías que creó la carga (registradas en ir_model_data, módulo pos_carga_masiva) se borran si ya no tienen
-- productos; las que existían antes (por ejemplo "Bebidas") no se tocan.
\set ON_ERROR_STOP on
BEGIN;
CREATE TEMP TABLE hist_orders ON COMMIT DROP AS
SELECT o.id FROM pos_order o JOIN pos_session s ON s.id = o.session_id WHERE s.name = 'POS Web .NET/HISTORICO';
DELETE FROM pos_payment WHERE pos_order_id IN (SELECT id FROM hist_orders);
DELETE FROM pos_order_line WHERE order_id IN (SELECT id FROM hist_orders);
DELETE FROM pos_order WHERE id IN (SELECT id FROM hist_orders);
DELETE FROM pos_session WHERE name = 'POS Web .NET/HISTORICO';

-- Productos sintéticos vendidos en ventas reales: se archivan en lugar de borrarse.
CREATE TEMP TABLE gen_vendidos ON COMMIT DROP AS
SELECT DISTINCT pp.id AS product_id, pp.product_tmpl_id
FROM product_product pp
JOIN pos_order_line l ON l.product_id = pp.id
WHERE pp.default_code LIKE 'GEN-%';
UPDATE product_product SET active = false, default_code = 'GENV-' || substr(default_code, 5), barcode = NULL
WHERE id IN (SELECT product_id FROM gen_vendidos);
UPDATE product_template SET active = false, available_in_pos = false, default_code = 'GENV-' || substr(default_code, 5), barcode = NULL
WHERE id IN (SELECT product_tmpl_id FROM gen_vendidos);

CREATE TEMP TABLE gen_tmpl ON COMMIT DROP AS SELECT id FROM product_template WHERE default_code LIKE 'GEN-%';
DELETE FROM product_taxes_rel WHERE prod_id IN (SELECT id FROM gen_tmpl);
DELETE FROM pos_category_product_template_rel WHERE product_template_id IN (SELECT id FROM gen_tmpl);
DELETE FROM product_product WHERE product_tmpl_id IN (SELECT id FROM gen_tmpl);
DELETE FROM product_template WHERE id IN (SELECT id FROM gen_tmpl);

DELETE FROM pos_category c
USING ir_model_data d
WHERE d.module = 'pos_carga_masiva' AND d.model = 'pos.category' AND d.res_id = c.id
  AND NOT EXISTS (SELECT 1 FROM pos_category_product_template_rel r WHERE r.pos_category_id = c.id);
DELETE FROM ir_model_data d
WHERE d.module = 'pos_carga_masiva' AND d.model = 'pos.category' AND NOT EXISTS (SELECT 1 FROM pos_category c WHERE c.id = d.res_id);
SELECT count(*) AS productos_sinteticos_archivados_por_tener_ventas_reales FROM gen_vendidos;
COMMIT;
