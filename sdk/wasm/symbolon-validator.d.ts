/**
 * TypeScript definitions for Symbolon WebAssembly & In-Browser Offline License Validator SDK
 */

export interface ValidatorOptions {
  /**
   * JSON Web Key Set containing server public keys (string or parsed object).
   */
  jwks?: string | JwksDocument;

  /**
   * Expected audience (e.g. application identifier).
   */
  expectedAudience?: string;

  /**
   * Clock skew tolerance in seconds (default: 300).
   */
  clockSkewSeconds?: number;
}

export interface JwkKey {
  kty: string;
  crv?: string;
  x?: string;
  y?: string;
  kid?: string;
  alg?: string;
  use?: string;
}

export interface JwksDocument {
  keys: JwkKey[];
}

export class ValidationResult {
  readonly isValid: boolean;
  readonly failureReason: string | null;
  readonly customer: string | null;
  readonly customerRef: string | null;
  readonly product: string | null;
  readonly licenseType: string | null;
  readonly licenseKey: string | null;
  readonly maxSeats: number | null;
  readonly expiresAt: number | null;
  readonly isExpired: boolean;
  readonly daysRemaining: number | null;
  readonly machineMatch: boolean;
  readonly isNodeLocked: boolean;
  readonly boundFingerprint: string | null;
  readonly features: string[];
  readonly verifiedAlgs: string[];
  readonly issuer: string | null;
  readonly subject: string | null;
  readonly issuedAt: number | null;

  hasFeature(featureCode: string): boolean;
  readonly expiresAtFormatted: string;
}

export class SymbolonOfflineValidator {
  constructor(options?: ValidatorOptions);

  setJwks(jwks: string | JwksDocument): void;

  validate(
    pemOrJws: string,
    fingerprint?: string | null,
    customNowSeconds?: number | null
  ): Promise<ValidationResult>;
}

export function generateBrowserFingerprint(): Promise<string>;

export function validateLic34Key(keyString: string): boolean;

export function unwrapPemArmor(text: string): string;

export default SymbolonOfflineValidator;
