# Prepara Odoo 20 Community para el POS Web (.NET). Se ejecuta DENTRO de "odoo shell".
#
# Es idempotente: puede ejecutarse varias veces sin duplicar datos. En cada ejecución genera una
# API key NUEVA para el usuario de integración (las anteriores con el mismo nombre se eliminan).
#
#   Docker (PowerShell):  Get-Content .\odoo\setup_pos.py | docker compose -f odoo/docker-compose.yml exec -T odoo odoo shell -d pos --no-http
#   Docker (bash):        docker compose -f odoo/docker-compose.yml exec -T odoo odoo shell -d pos --no-http < odoo/setup_pos.py
#
# Al final imprime una línea que empieza con @@POS_SETUP@@ seguida de un JSON con los identificadores
# reales (sesión, productos, método de pago) y la API key. Los scripts setup.ps1 / setup.sh la leen
# y la guardan en los user-secrets del backend (la clave nunca se escribe en el repositorio).
import json
from datetime import datetime, timedelta

OUTPUT_MARKER = "@@POS_SETUP@@"
POS_CONFIG_NAME = "POS Web .NET"
INTEGRATION_LOGIN = "pos.integracion"
INTEGRATION_GROUP = "POS Web - Integración (API)"
API_KEY_NAME = "POS Web .NET backend"
API_KEY_DAYS = 180
LANG = "es_419"

company = env.company
currency = company.currency_id
large_unit_currency = currency.decimal_places == 0 or currency.name in ("COP", "CLP", "PYG", "JPY")


def log(message):
    print(f"[setup] {message}")


def price(cop, usd):
    """Precios de ejemplo coherentes con la moneda de la compañía."""
    return float(cop) if large_unit_currency else float(usd)


# 1. Idioma español (mensajes de error de Odoo en español) -------------------------------------
lang = env['res.lang'].with_context(active_test=False).search([('code', '=', LANG)], limit=1)
if lang and not lang.active:
    env['res.lang']._activate_lang(LANG)
    env['ir.module.module'].search([('state', '=', 'installed')])._update_translations(filter_lang=LANG)
    log(f"Idioma {LANG} activado")


# 2. Impuestos de venta -------------------------------------------------------------------------
def ensure_tax(name, amount, included):
    tax = env['account.tax'].search([('name', '=', name), ('company_id', '=', company.id)], limit=1)
    if tax:
        return tax  # ya existe: no se modifica (Odoo protege los impuestos usados en ventas)
    values = {
        'name': name,
        'amount_type': 'percent',
        'amount': amount,
        'type_tax_use': 'sale',
        'price_include_override': 'tax_included' if included else 'tax_excluded',
        'company_id': company.id,
    }
    return env['account.tax'].create(values)


iva19 = ensure_tax("IVA 19 % (POS Web)", 19.0, False)
iva5 = ensure_tax("IVA 5 % (POS Web)", 5.0, False)
iva19_incluido = ensure_tax("IVA 19 % incluido (POS Web)", 19.0, True)


# 3. Categorías y productos ---------------------------------------------------------------------
def ensure_pos_category(name):
    category = env['pos.category'].search([('name', '=', name)], limit=1)
    return category or env['pos.category'].create({'name': name})


bebidas = ensure_pos_category("Bebidas")
comidas = ensure_pos_category("Comidas")
panaderia = ensure_pos_category("Panadería")
snacks = ensure_pos_category("Snacks")

PRODUCTS = [
    # código, nombre, precio COP, precio USD, impuestos, categoría, código de barras, disponible en POS
    ("CAF-001", "Café americano 12 oz", 4500, 3.50, iva19, bebidas, "7701000000011", True),
    ("CAF-002", "Capuchino 12 oz", 6900, 4.75, iva19, bebidas, "7701000000028", True),
    ("BEB-001", "Agua mineral 600 ml", 3000, 1.99, iva19_incluido, bebidas, "7701000000035", True),
    ("BEB-002", "Jugo natural de naranja", 7500, 5.25, iva5, bebidas, "7701000000042", True),
    ("COM-001", "Sándwich de pollo", 15800, 8.90, iva19, comidas, "7701000000059", True),
    ("COM-002", "Ensalada César", 18900, 10.50, iva19, comidas, "7701000000066", True),
    ("PAN-001", "Croissant de mantequilla", 5200, 2.80, iva5, panaderia, "7701000000073", True),
    ("PAN-002", "Pan de bono", 2500, 1.25, None, panaderia, "7701000000080", True),
    ("SNK-001", "Galletas artesanales", 3900, 2.15, iva19_incluido, snacks, "7701000000097", True),
    ("SNK-002", "Papas fritas 45 g", 3200, 1.60, iva19, snacks, "7701000000103", True),
    ("OLD-001", "Producto descontinuado (no POS)", 1000, 0.99, iva19, snacks, "7701000000110", False),
]

products = {}
for code, name, cop, usd, tax, category, barcode, available in PRODUCTS:
    template = env['product.template'].with_context(active_test=False).search([('default_code', '=', code)], limit=1)
    values = {
        'name': name,
        'default_code': code,
        'barcode': barcode,
        'list_price': price(cop, usd),
        'type': 'consu',
        'is_storable': True,  # con Inventario instalado, Odoo lleva las existencias y el POS las muestra
        'sale_ok': True,
        'available_in_pos': available,
        'taxes_id': [(6, 0, tax.ids if tax else [])],
        'pos_categ_ids': [(6, 0, category.ids)],
        'active': True,
    }
    if template:
        template.write(values)
    else:
        template = env['product.template'].create(values)
    products[code] = template.product_variant_id.id
log(f"{len(products)} productos listos")

# Existencias iniciales en el almacén principal. Solo se cargan si el producto aún no tiene existencias, para que
# volver a ejecutar el script no borre las ventas que ya descontaron unidades. PAN-002 queda en 0 a propósito.
INITIAL_STOCK = {"CAF-001": 120, "CAF-002": 80, "BEB-001": 200, "BEB-002": 60, "COM-001": 35, "COM-002": 25,
                 "PAN-001": 48, "PAN-002": 0, "SNK-001": 90, "SNK-002": 150}
warehouse = env['stock.warehouse'].search([('company_id', '=', company.id)], limit=1)
if warehouse:
    stock_location = warehouse.lot_stock_id
    for code, qty in INITIAL_STOCK.items():
        product = env['product.product'].browse(products[code])
        has_quants = env['stock.quant'].search_count([('product_id', '=', product.id), ('location_id', 'child_of', stock_location.id)])
        if qty > 0 and not has_quants:
            env['stock.quant'].with_context(inventory_mode=True).create({
                'product_id': product.id, 'location_id': stock_location.id, 'inventory_quantity': qty,
            }).action_apply_inventory()
    log(f"Existencias cargadas en {stock_location.complete_name}")


# 4. Diarios y métodos de pago ------------------------------------------------------------------
def ensure_journal(name, journal_type, code):
    journal = env['account.journal'].search([('code', '=', code), ('company_id', '=', company.id)], limit=1)
    return journal or env['account.journal'].create({
        'name': name, 'type': journal_type, 'code': code, 'company_id': company.id,
    })


cash_journal = ensure_journal("Efectivo POS Web", 'cash', 'CSHW')
bank_journal = env['account.journal'].search([('type', '=', 'bank'), ('company_id', '=', company.id)], limit=1) \
    or ensure_journal("Banco POS Web", 'bank', 'BNKW')


def ensure_payment_method(name, journal):
    method = env['pos.payment.method'].search([('name', '=', name), ('company_id', '=', company.id)], limit=1)
    values = {
        'name': name,
        'journal_id': journal.id,
        'type': journal.type,
        'company_id': company.id,
        'payment_method_type': 'none',
    }
    if method:
        # Odoo no permite modificar un método de pago usado por una sesión abierta: se reutiliza tal cual.
        if not method.open_session_ids:
            method.write(values)
        return method
    return env['pos.payment.method'].create(values)


cash_method = ensure_payment_method("Efectivo", cash_journal)
card_method = ensure_payment_method("Tarjeta (datáfono externo)", bank_journal)


# 5. Punto de venta -----------------------------------------------------------------------------
config = env['pos.config'].search([('name', '=', POS_CONFIG_NAME)], limit=1)
config_values = {
    'name': POS_CONFIG_NAME,
    'payment_method_ids': [(6, 0, [cash_method.id, card_method.id])],
    'cash_rounding': False,
}
if config:
    if not config.current_session_id:
        config.write(config_values)
else:
    config = env['pos.config'].create(config_values)
log(f"Punto de venta «{config.name}» (id {config.id})")


# 6. Usuario de integración y su grupo (duración máxima de la API key) ---------------------------
integration_group = env['res.groups'].search([('name', '=', INTEGRATION_GROUP)], limit=1) \
    or env['res.groups'].create({'name': INTEGRATION_GROUP, 'api_key_duration': float(API_KEY_DAYS)})
integration_group.api_key_duration = float(API_KEY_DAYS)

pos_user_group = env.ref('point_of_sale.group_pos_user')
user = env['res.users'].with_context(active_test=False).search([('login', '=', INTEGRATION_LOGIN)], limit=1)
user_values = {
    'name': "Integración POS Web (.NET)",
    'login': INTEGRATION_LOGIN,
    'active': True,
    'company_id': company.id,
    'company_ids': [(6, 0, [company.id])],
    # Contacto / Creación: para registrar clientes con nombre y cédula, como lo hace un cajero en la caja de Odoo.
    'group_ids': [(4, env.ref('base.group_user').id), (4, pos_user_group.id), (4, integration_group.id),
                  (4, env.ref('base.group_partner_manager').id)],
}
if lang:
    user_values['lang'] = LANG
if user:
    user.write(user_values)
else:
    user = env['res.users'].with_context(no_reset_password=True).create(user_values)
log(f"Usuario de integración «{user.login}» (id {user.id})")


# 7. Sesión POS abierta ---------------------------------------------------------------------------
session = env['pos.session'].search([('config_id', '=', config.id), ('state', '!=', 'closed')], limit=1)
if not session:
    session = env['pos.session'].create({'config_id': config.id, 'user_id': user.id})
if session.state == 'opening_control':
    session.set_opening_control(0, "Apertura automática para el POS Web (.NET)")
log(f"Sesión {session.name} en estado {session.state}")


# 8. API key nueva para el backend ---------------------------------------------------------------
ApiKeys = env['res.users.apikeys'].sudo()
ApiKeys.search([('user_id', '=', user.id), ('name', '=', API_KEY_NAME)]).unlink()
api_key = env['res.users.apikeys'].with_user(user)._generate(
    'rpc', API_KEY_NAME, datetime.now() + timedelta(days=API_KEY_DAYS - 1))

env.cr.commit()
log("Cambios guardados")

print(OUTPUT_MARKER + json.dumps({
    'database': env.cr.dbname,
    'apiKey': api_key,
    'apiKeyExpiresInDays': API_KEY_DAYS - 1,
    'integrationUser': user.login,
    'posConfigId': config.id,
    'posConfigName': config.name,
    'sessionId': session.id,
    'sessionName': session.name,
    'sessionState': session.state,
    'currency': currency.name,
    'taxRounding': company.tax_calculation_rounding_method,
    'paymentMethods': {'cash': cash_method.id, 'card': card_method.id},
    'products': products,
}))
