import { describe, expect, it } from 'vitest';
import { customerLabel, isValidCedula, sanitizeCedula, sanitizeCustomerName, validateCustomer } from '../../src/domain/customer';

describe('Cliente de la venta', () => {
  it.each(['1710034065', '0926687856', '3050000003'])('acepta la cédula válida %s', (cedula) => {
    expect(isValidCedula(cedula)).toBe(true);
  });

  it.each([
    ['1710034064', 'dígito verificador incorrecto'],
    ['171003406', '9 dígitos'],
    ['2510034065', 'provincia 25'],
    ['1770034065', 'tercer dígito 7'],
    ['17100340a5', 'letras'],
    ['2222222222', 'todos los dígitos iguales'],
  ])('rechaza %s (%s)', (cedula) => {
    expect(isValidCedula(cedula)).toBe(false);
  });

  it('valida nombre y cédula con mensajes claros para el cajero', () => {
    expect(validateCustomer('  Ana   Pérez ', '1710034065')).toEqual({
      ok: true,
      customer: { kind: 'identified', name: 'Ana Pérez', identification: '1710034065' },
    });
    expect(validateCustomer('A', '17100')).toEqual({
      ok: false,
      errors: { name: 'Escriba nombre y apellido del cliente.', identification: 'La cédula tiene 10 dígitos (lleva 5).' },
    });
    expect(validateCustomer('Ana Pérez', '1710034064')).toEqual({ ok: false, errors: { identification: 'La cédula no es válida: revise los dígitos.' } });
  });

  it('el nombre exige nombre y apellido con letras; acepta tildes, guion y apóstrofo', () => {
    expect(validateCustomer('Ana', '1710034065')).toEqual({ ok: false, errors: { name: 'Escriba nombre y apellido del cliente.' } });
    expect(validateCustomer('Ana 123', '1710034065')).toEqual({ ok: false, errors: { name: 'El nombre solo puede tener letras, espacios, guion o apóstrofo.' } });
    expect(validateCustomer("María José O'Neil-Ríos", '1710034065').ok).toBe(true);
  });

  it('al escribir, el nombre descarta números y símbolos y la cédula todo lo que no sea dígito', () => {
    expect(sanitizeCustomerName('Ana2 Pérez$%')).toBe('Ana Pérez');
    expect(sanitizeCedula('17-1003.4065 99')).toBe('1710034065');
  });

  it('muestra "Consumidor final" cuando la venta no tiene cliente', () => {
    expect(customerLabel(null)).toBe('Consumidor final');
    expect(customerLabel({ name: 'Ana Pérez', identification: '1710034065' })).toBe('Ana Pérez (CI 1710034065)');
  });
});
