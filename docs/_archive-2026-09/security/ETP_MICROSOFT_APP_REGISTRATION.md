# Microsoft owner-authentication configuration guide

Status: implementation-ready guidance; no app registration values are stored yet
Verified against Microsoft documentation: 2026-08-27

## Purpose

This registration belongs only to the owner-controlled **ETP Licence Administrator**. The store ETP application does not sign in to Microsoft during normal operation and does not receive Microsoft credentials.

## Registration

1. Sign in to the Microsoft Entra admin center using the owner-controlled administration account.
2. Open **Identity → Applications → App registrations → New registration**.
3. Name it `ETP Licence Administrator`.
4. Select **Accounts in any organizational directory and personal Microsoft accounts**. This maps to manifest `signInAudience: AzureADandPersonalMicrosoftAccount` and supports Outlook.com/personal Microsoft accounts plus a future approved work account.
5. Register the application.
6. Record the **Application (client) ID**. The client ID is an identifier, not a secret, and may be included in owner-utility configuration.
7. Do not create a client secret. A Windows desktop app is a public client and cannot securely retain one.

## Authentication platform

1. Open **Authentication → Add a platform → Mobile and desktop applications**.
2. Configure the redirect required by the selected MSAL mode:
   - system browser/current .NET desktop default: `http://localhost` via `.WithDefaultRedirectUri()`;
   - WAM/broker: follow the then-current MSAL broker registration instructions; WAM may not require the same redirect.
3. Enable public-client flows only when required by the final selected MSAL flow and current portal guidance.
4. Do not enable implicit grant for access or ID tokens.

The exact MSAL/WAM configuration must be proven with the final package versions before production registration is frozen. Redirect URIs must exactly match the application registration.

## Authority and accounts

Default engineering selection:

```text
Authority audience: common
Registration audience: AzureADandPersonalMicrosoftAccount
```

If final policy permits personal Microsoft accounts only, change both registration and code coherently after validating the current Microsoft requirement; use the `consumers` authority rather than relying on email-domain text.

## Permissions

Request only OpenID Connect identity scopes:

```text
openid
profile
```

Do not request mail, calendar, contacts, files, directory, group or administrative permissions. Do not call Microsoft Graph merely to obtain the display email. The authorization anchor is the validated `(tid, oid)` identity pair. `name` and email-like claims are display-only.

If the selected MSAL package at implementation time requires a resource scope for interactive acquisition, the implementation gate must document that behavior before adding any permission. The minimal permitted fallback is delegated `User.Read`, with no Graph API call; broader scopes are prohibited without a new security decision.

## Owner allowlist provisioning

1. Run a one-time identity-enrollment build on the owner-controlled PC.
2. Authenticate the intended owner through Microsoft.
3. display the tenant/object identity fingerprint without storing the token;
4. independently verify the account and record the normalized `(tid, oid)` in owner-only protected configuration;
5. repeat for each additional approved owner;
6. require a documented reason and an existing approved owner for changes.

Never authorize solely by `preferred_username`, `name`, Outlook address or other mutable display text.

## Token handling

- Use `Microsoft.Identity.Client`; do not implement OAuth/OIDC manually.
- Use interactive authentication and provide the WPF parent-window handle.
- Keep tokens in memory for the issuance session by default.
- Clear the selected account/session when the issuance workflow closes.
- Never log or persist access, refresh or ID tokens.
- If persistent cache is later approved, use `Microsoft.Identity.Client.Extensions.Msal` with current-user OS protection and keep it on the owner PC only.
- Microsoft MFA, Conditional Access and account recovery remain Microsoft's responsibility.

## Safe configuration values

Safe to include in owner-utility configuration:

- client ID;
- authority/audience name;
- redirect mode/URI;
- requested identity scopes;
- non-secret product identifier.

Never include:

- Microsoft password;
- client secret;
- access/refresh/ID token;
- production private licence-signing key;
- DPAPI plaintext activation secret;
- owner allowlist in the store application.

## Acceptance checklist

- Outlook.com owner can authenticate.
- Microsoft MFA challenge is handled by Microsoft UI.
- unapproved Microsoft account authenticates but is denied authorization.
- cancellation and no-network errors are distinct.
- no password field exists in either ETP application.
- token/claim logging is disabled.
- no Graph API request occurs.
- signing operation is unavailable until recent approved-owner authentication succeeds.

## Primary references

- https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-app-configuration
- https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-application-configuration
- https://learn.microsoft.com/en-us/entra/identity-platform/supported-accounts-validation
- https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc
- https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference
- https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization
