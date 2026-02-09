const DEFAULT_API_BASE_URL = "http://localhost:5072/api";

function normalizeApiBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL;

export const API_BASE_URL = normalizeApiBaseUrl(
  typeof configuredApiBaseUrl === "string" && configuredApiBaseUrl.trim().length > 0
    ? configuredApiBaseUrl.trim()
    : DEFAULT_API_BASE_URL
);

