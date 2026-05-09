# TrakingTool

[![Deploy PWA to GitHub Pages](https://github.com/Roots-Insurtech/TrakingTool/actions/workflows/deploy.yml/badge.svg)](https://github.com/Roots-Insurtech/TrakingTool/actions/workflows/deploy.yml)

PWA Blazor WebAssembly per il tracking delle attività, autenticata con Microsoft Entra ID e con persistenza dati su OneDrive (cartella App).

🌐 **App live**: https://roots-insurtech.github.io/TrakingTool/

## Stack

- .NET 9 / Blazor WebAssembly
- MudBlazor (UI, tema dark "Notion-like")
- MSAL (`Microsoft.Authentication.WebAssembly.Msal`) per il login Entra ID
- Microsoft Graph (`Files.ReadWrite.AppFolder`) per persistere i dati nella OneDrive dell'utente
- Service worker + manifest → installabile come PWA su desktop e mobile

## Sviluppo locale

```bash
dotnet restore
dotnet run
```

L'app si avvia su `https://localhost:5001` (o porta indicata da `launchSettings.json`).

Per testare l'autenticazione Entra in locale, l'app registration deve avere registrato anche il redirect URI `https://localhost:<porta>/authentication/login-callback` come piattaforma "Single-page application".

## Pubblicazione

Il deploy su GitHub Pages è automatico al push su `main` tramite il workflow `.github/workflows/deploy.yml`. Il workflow:

1. Pubblica il progetto in `Release`
2. Patcha il `<base href>` da `/` a `/TrakingTool/` (subpath di GitHub Pages)
3. Aggiunge `.nojekyll` (altrimenti GitHub Pages filtra `_framework/` e `_content/`)
4. Copia `index.html` come `404.html` per il fallback del routing client-side
5. Carica l'artifact e lancia `actions/deploy-pages@v4`

## Configurazione Entra ID

`wwwroot/appsettings.json` punta a `login.microsoftonline.com/common`. L'app registration in Entra deve avere:

- **Platform**: Single-page application
- **Redirect URI**: `https://roots-insurtech.github.io/TrakingTool/authentication/login-callback`
- **Post-logout URI**: `https://roots-insurtech.github.io/TrakingTool/authentication/logout-callback`
- **API permissions** (Microsoft Graph, delegated): `User.Read`, `Files.ReadWrite.AppFolder`
