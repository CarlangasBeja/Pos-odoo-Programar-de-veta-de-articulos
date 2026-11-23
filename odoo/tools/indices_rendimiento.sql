-- Índices para que las consultas del POS respondan rápido con un millón de registros.
-- Cada índice corresponde a una consulta que Odoo genera para la API (SQL observado con log_min_duration_statement;
-- ver docs/RENDIMIENTO_Y_RED.md). Odoo no borra índices que no creó él; se pueden quitar con DROP INDEX sin afectarlo.
-- Prefijo "pos_web_" para reconocerlos. Es repetible (IF NOT EXISTS).
\set ON_ERROR_STOP on
\timing on
CREATE EXTENSION IF NOT EXISTS pg_trgm;
SET maintenance_work_mem = '256MB';

-- Catálogo: búsqueda por código y código de barras con ilike '%texto%' (los btree de Odoo no sirven para '%...%').
-- La API busca en la plantilla (nombre, código y código de barras en la misma tabla) para que PostgreSQL combine
-- estos índices con el índice trigrama del nombre que ya crea Odoo (product_template__name_index).
CREATE INDEX IF NOT EXISTS pos_web_template_default_code_trgm ON product_template USING gin (default_code gin_trgm_ops);
CREATE INDEX IF NOT EXISTS pos_web_template_barcode_trgm ON product_template USING gin (barcode gin_trgm_ops);
-- Y en la variante, para consultas directas por código (por ejemplo, desde Odoo o reportes).
CREATE INDEX IF NOT EXISTS pos_web_product_default_code_trgm ON product_product USING gin (default_code gin_trgm_ops);
CREATE INDEX IF NOT EXISTS pos_web_product_barcode_trgm ON product_product USING gin (barcode gin_trgm_ops);

-- Catálogo: orden por nombre traducido (ORDER BY COALESCE(name->>'es_419', name->>'en_US')) solo de productos del POS.
CREATE INDEX IF NOT EXISTS pos_web_template_pos_name ON product_template ((COALESCE(name ->> 'es_419', name ->> 'en_US')), id)
    WHERE available_in_pos IS TRUE AND sale_ok IS TRUE;
-- Catálogo: orden por precio.
CREATE INDEX IF NOT EXISTS pos_web_template_pos_price ON product_template (list_price, id)
    WHERE available_in_pos IS TRUE AND sale_ok IS TRUE;

-- Historial: búsqueda por nombre de orden, recibo y producto vendido.
CREATE INDEX IF NOT EXISTS pos_web_order_name_trgm ON pos_order USING gin (name gin_trgm_ops);
CREATE INDEX IF NOT EXISTS pos_web_order_reference_trgm ON pos_order USING gin (pos_reference gin_trgm_ops);
CREATE INDEX IF NOT EXISTS pos_web_order_line_product_name_trgm ON pos_order_line USING gin (full_product_name gin_trgm_ops);
-- Historial: ventas de un punto de venta, más recientes primero.
CREATE INDEX IF NOT EXISTS pos_web_order_config_date ON pos_order (config_id, date_order DESC, id DESC);

ANALYZE product_template;
ANALYZE product_product;
ANALYZE pos_order;
ANALYZE pos_order_line;
