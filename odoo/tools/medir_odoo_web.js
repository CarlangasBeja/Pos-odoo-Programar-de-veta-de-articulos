// Mide cuánto tardan las pantallas de la interfaz web de Odoo con un millón de registros.
// Uso (requiere Node y el paquete playwright-core):  node odoo/tools/medir_odoo_web.js [carpeta-capturas]
// Usa el usuario admin/admin que crea odoo/setup.ps1 en la instancia local de pruebas.
const { chromium } = require('playwright-core');
const path = require('path');
const OUT = process.argv[2];
const B = 'http://localhost:8069';

(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  const page = await (await browser.newContext({ viewport: { width: 1440, height: 860 }, locale: 'es-419' })).newPage();
  await page.goto(`${B}/web/login`);
  await page.fill('input[name=login]', 'admin');
  await page.fill('input[name=password]', 'admin');
  await page.click('button[type=submit]');
  await page.waitForURL(/\/odoo/, { timeout: 30000 });

  // Una pantalla está lista cuando no queda ninguna llamada de datos de Odoo en curso durante 400 ms.
  let enCurso = 0;
  const esDatos = (r) => r.url().includes('/web/dataset/') || r.url().includes('/web/action/');
  page.on('request', (r) => { if (esDatos(r)) enCurso++; });
  page.on('requestfinished', (r) => { if (esDatos(r)) enCurso--; });
  page.on('requestfailed', (r) => { if (esDatos(r)) enCurso--; });
  const listo = async () => {
    await page.waitForTimeout(150);
    let quieto = 0;
    const limite = Date.now() + 120000;
    while (quieto < 400 && Date.now() < limite) {
      await page.waitForTimeout(50);
      quieto = enCurso <= 0 ? quieto + 50 : 0;
    }
  };
  const medir = async (nombre, accion, captura) => {
    const t = Date.now();
    await accion();
    await listo();
    const ms = Date.now() - t - 400; // se descuenta la espera de verificación
    const contador = await page.locator('.o_pager_counter').first().innerText().catch(() => '');
    console.log(`${nombre.padEnd(52)} ${String(ms).padStart(6)} ms   ${contador.replace(/\s+/g, ' ')}`);
    if (OUT && captura) {
      await page.addStyleTag({ content: '.o_tour_pointer{display:none!important}' });
      await page.screenshot({ path: path.join(OUT, captura + '.png') });
    }
    return ms;
  };
  const buscar = async (texto) => {
    // Quita los filtros anteriores (Retroceso en el buscador vacío) para medir cada búsqueda por separado.
    const input = page.locator('.o_searchview_input');
    await input.fill('');
    for (let i = 0; i < 4 && (await page.locator('.o_searchview_facet').count()) > 0; i++) {
      await input.press('Backspace');
      await listo();
    }
    await input.fill(texto);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(150);
  };

  await medir('Productos del POS (lista)', () => page.goto(`${B}/odoo/action-point_of_sale.product_template_action_pos_product?view_type=list`), 'odoo_web_productos_1m');
  await medir('Productos: buscar "GEN-0765432"', () => buscar('GEN-0765432'));
  await medir('Productos: buscar "chocolate"', () => buscar('chocolate'), 'odoo_web_productos_busqueda');
  await medir('Órdenes del POS (lista)', () => page.goto(`${B}/odoo/action-point_of_sale.action_pos_pos_form`), 'odoo_web_ordenes_1m');
  await medir('Órdenes: buscar "Histórico - 0500000"', () => buscar('Histórico - 0500000'));
  await medir('Órdenes: abrir el detalle', () => page.locator('.o_data_row').first().click(), 'odoo_web_orden_detalle');
  await medir('Productos: buscar "Sierra 765432" (raro)', async () => { await page.goto(`${B}/odoo/action-point_of_sale.product_template_action_pos_product?view_type=list`); await listo(); await buscar('Sierra 765432'); });
  await medir('Tablero del punto de venta (código de Odoo)', () => page.goto(`${B}/odoo/action-point_of_sale.action_pos_config_kanban`));
  await browser.close();
})().catch((e) => { console.error('FALLO', e.message); process.exit(1); });
