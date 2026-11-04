# Genera vectores de prueba con el MOTOR REAL de impuestos de Odoo 20.
#
# Se ejecuta dentro de "odoo shell" y NO guarda nada en la base (al final hace rollback).
# El resultado (JSON) se usa en backend/tests/Pos.UnitTests/Domain/TaxCalculatorOdooParityTests.cs
# para demostrar que el cálculo de totales del backend .NET coincide con el de Odoo.
#
#   Docker:   Get-Content odoo/tools/generate_tax_vectors.py | docker compose -f odoo/docker-compose.yml exec -T odoo odoo shell -d pos --no-http > vectors.json
#   Fuente:   ./odoo-bin shell -c odoo.conf -d pos --no-http < odoo/tools/generate_tax_vectors.py
import json
import random

OUTPUT_MARKER = "@@VECTORS@@"
CASES_PER_SCENARIO = 150
random.seed(2026)

AccountTax = env['account.tax']
company = env.company


def make_tax(name, amount, included):
    return AccountTax.create({
        'name': name,
        'amount_type': 'percent',
        'amount': amount,
        'type_tax_use': 'sale',
        'price_include_override': 'tax_included' if included else 'tax_excluded',
        'company_id': company.id,
    })


taxes = {
    'iva19': make_tax('PARITY IVA 19 %', 19.0, False),
    'iva5': make_tax('PARITY IVA 5 %', 5.0, False),
    'inc8': make_tax('PARITY INC 8 %', 8.0, False),
    'iva19inc': make_tax('PARITY IVA 19 % incluido', 19.0, True),
    'exento': make_tax('PARITY Exento 0 %', 0.0, False),
}
tax_sets = [[], ['iva19'], ['iva5'], ['iva19', 'inc8'], ['iva19inc'], ['exento']]


def random_line(cents):
    if cents:
        price = round(random.uniform(0.05, 450.0), 2)
    else:
        price = float(random.choice([500, 1200, 2500, 3900, 4750, 9990, 15800, 26900, 49999, 120000]))
    quantity = random.choice([1, 1, 2, 3, 5, 7, 10, 12, 0.5, 1.25, 2.375])
    discount = random.choice([0, 0, 0, 3, 5, 10, 12.5, 15, 33.33])
    tax_keys = random.choice(tax_sets)
    return {'priceUnit': price, 'quantity': quantity, 'discount': discount, 'taxes': tax_keys}


def odoo_totals(lines, currency, rounding_method):
    company.tax_calculation_rounding_method = rounding_method
    base_lines = []
    for line in lines:
        tax_ids = AccountTax.browse([taxes[k].id for k in line['taxes']])
        base_lines.append(AccountTax._prepare_base_line_for_taxes_computation(
            None,
            price_unit=line['priceUnit'],
            quantity=line['quantity'],
            discount=line['discount'],
            tax_ids=tax_ids,
            currency_id=currency,
            rate=1.0,
        ))
    AccountTax._add_tax_details_in_base_lines(base_lines, company)
    AccountTax._round_base_lines_tax_details(base_lines, company)
    summary = AccountTax._get_tax_totals_summary(base_lines=base_lines, currency=currency, company=company)
    return {
        'untaxed': round(summary['base_amount_currency'], 6),
        'tax': round(summary['tax_amount_currency'], 6),
        'total': round(summary['total_amount_currency'], 6),
    }


currencies = [
    ('USD', env.ref('base.USD'), True),
    ('JPY', env.ref('base.JPY'), False),  # redondeo a la unidad, como pesos sin centavos
]

vectors = []
for code, currency, cents in currencies:
    for rounding_method in ('round_globally', 'round_per_line'):
        for _ in range(CASES_PER_SCENARIO):
            lines = [random_line(cents) for _ in range(random.randint(1, 6))]
            vectors.append({
                'currency': code,
                'currencyRounding': currency.rounding,
                'roundingMethod': rounding_method,
                'lines': lines,
                'expected': odoo_totals(lines, currency, rounding_method),
            })

output = {
    'odooVersion': __import__('odoo').release.version,
    'taxes': {
        key: {'amount': tax.amount, 'priceIncluded': tax.price_include}
        for key, tax in taxes.items()
    },
    'vectors': vectors,
}
env.cr.rollback()  # no se guarda nada en la base de datos
print(OUTPUT_MARKER + json.dumps(output))
