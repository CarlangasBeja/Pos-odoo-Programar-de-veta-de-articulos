import '@testing-library/jest-dom/vitest';

// Indica a React que las pruebas corren en un entorno que soporta act().
(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
