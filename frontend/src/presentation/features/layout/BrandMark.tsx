/** Marca: un tiquete de caja estilizado (borde inferior dentado). */
export function BrandMark({ size = 32 }: { readonly size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" className="brand-mark">
      <rect width="32" height="32" rx="8" fill="var(--primary)" />
      <path
        d="M10 7h12a1 1 0 0 1 1 1v17l-2-1.5-2 1.5-2-1.5-2 1.5-2-1.5-2 1.5-2-1.5V8a1 1 0 0 1 1-1Z"
        fill="var(--paper)"
      />
      <path d="M12.5 12h7M12.5 15.5h7M12.5 19h4" stroke="var(--primary)" strokeWidth="1.6" strokeLinecap="round" />
    </svg>
  );
}
