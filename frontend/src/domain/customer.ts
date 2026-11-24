/** Cliente de la venta: consumidor final (sin datos) o una persona con nombre y cédula. */
export type SaleCustomer =
  | { readonly kind: 'finalConsumer' }
  | { readonly kind: 'identified'; readonly name: string; readonly identification: string };

export const FINAL_CONSUMER: SaleCustomer = { kind: 'finalConsumer' };
export const FINAL_CONSUMER_LABEL = 'Consumidor final';
export const CEDULA_LENGTH = 10;
export const CUSTOMER_NAME_MAX = 80;

/** Cédula ecuatoriana: 10 dígitos, provincia 01 a 24 (o 30), tercer dígito menor que 6 y dígito verificador módulo 10. */
export function isValidCedula(value: string): boolean {
  if (!/^\d{10}$/.test(value)) return false;
  if (/^(\d)\1{9}$/.test(value)) return false; // 1111111111, 2222222222...: no son cédulas reales
  const digits = [...value].map(Number);
  const province = digits[0]! * 10 + digits[1]!;
  if ((province < 1 || province > 24) && province !== 30) return false;
  if (digits[2]! >= 6) return false;
  const sum = digits.slice(0, 9).reduce((acc, d, i) => {
    const product = d * (i % 2 === 0 ? 2 : 1);
    return acc + (product > 9 ? product - 9 : product);
  }, 0);
  return (10 - (sum % 10)) % 10 === digits[9];
}

export interface CustomerFieldErrors {
  readonly name?: string;
  readonly identification?: string;
}

export type CustomerValidation = { readonly ok: true; readonly customer: SaleCustomer } | { readonly ok: false; readonly errors: CustomerFieldErrors };

/** Valida los datos escritos por el cajero. Las mismas reglas las aplica el backend. */
export function validateCustomer(name: string, identification: string): CustomerValidation {
  const cleanName = name.trim().replace(/\s+/g, ' ');
  const cleanId = identification.trim();
  const errors: { name?: string; identification?: string } = {};
  if (cleanName.length > CUSTOMER_NAME_MAX) errors.name = `El nombre admite hasta ${CUSTOMER_NAME_MAX} caracteres.`;
  else if (!/^[\p{L} '-]*$/u.test(cleanName)) errors.name = 'El nombre solo puede tener letras, espacios, guion o apóstrofo.';
  else if (cleanName.split(' ').filter((word) => (word.match(/\p{L}/gu) ?? []).length >= 2).length < 2) errors.name = 'Escriba nombre y apellido del cliente.';
  if (!/^\d*$/.test(cleanId)) errors.identification = 'La cédula solo lleva números.';
  else if (cleanId.length !== CEDULA_LENGTH) errors.identification = `La cédula tiene ${CEDULA_LENGTH} dígitos (lleva ${cleanId.length}).`;
  else if (!isValidCedula(cleanId)) errors.identification = 'La cédula no es válida: revise los dígitos.';
  return errors.name || errors.identification
    ? { ok: false, errors }
    : { ok: true, customer: { kind: 'identified', name: cleanName, identification: cleanId } };
}

/** Mientras se escribe: deja solo letras, espacios, guion y apóstrofo (los números y símbolos no entran). */
export function sanitizeCustomerName(value: string): string {
  return value.replace(/[^\p{L} '-]/gu, '').replace(/\s{2,}/g, ' ').slice(0, CUSTOMER_NAME_MAX);
}

/** Mientras se escribe: deja solo dígitos, hasta 10. */
export function sanitizeCedula(value: string): string {
  return value.replace(/\D/g, '').slice(0, CEDULA_LENGTH);
}

/** Texto para la pantalla y la nota de venta. */
export function customerLabel(customer: { readonly name: string; readonly identification: string | null } | null | undefined): string {
  if (!customer) return FINAL_CONSUMER_LABEL;
  return customer.identification ? `${customer.name} (CI ${customer.identification})` : customer.name;
}
