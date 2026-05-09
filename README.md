# TrakingTool

[![Deploy PWA to GitHub Pages](https://github.com/Roots-Insurtech/TrakingTool/actions/workflows/deploy.yml/badge.svg)](https://github.com/Roots-Insurtech/TrakingTool/actions/workflows/deploy.yml)

PWA Blazor WebAssembly per il tracking delle attività, autenticata con Microsoft Entra ID e con persistenza dati su OneDrive (cartella App). Pensata per uso single-user multi-device, **funziona offline** con coda di sincronizzazione e brandizzata Roots Insurtech.

🌐 **App live**: https://roots-insurtech.github.io/TrakingTool/

## Stack

- **.NET 9 / Blazor WebAssembly** — SPA 100% client-side
- **MudBlazor 9.4** — UI components, tema dark "Notion-like" (`Theme/NotionDarkTheme.cs`)
- **MSAL** (`Microsoft.Authentication.WebAssembly.Msal`) — login con Entra ID, modalità redirect
- **Microsoft Graph** (`Files.ReadWrite.AppFolder`) — persistenza dati nella OneDrive dell'utente
- **Blazored.LocalStorage** — cache offline e persistenza filtri UI
- **Service worker + manifest** — installabile come PWA su desktop e mobile

## Architettura sync (cache-first)

I dati vivono in OneDrive (un file JSON per anno: `entries-{anno}.json` sotto `/me/drive/special/approot`). Per garantire latenza zero e funzionalità offline, l'accesso passa sempre dal **`CachedEntryRepository`** (DI: `IEntryRepository`):

```
UI Pages
  ↓
CachedEntryRepository  ←  IEntryRepository
  ├── LocalEntryRepository  (LocalStorage, blob per anno)
  ├── OneDriveEntryRepository  (Graph API)
  ├── PendingSyncStore  (id pending + delete queue)
  ├── SyncStateService  (Idle/Syncing/Offline/Error + pending count)
  └── OnlineStatusService  (window.online/offline via JS interop)
```

**Read** = stale-while-revalidate: ritorno immediato dalla cache locale, refresh in background da OneDrive con throttle (8s minimo) per evitare loop. Il merge tiene gli entry pending locali sopra il remoto (anti-race con input utente).

**Write** = write-through: salvo locale → marco pending → tento push remoto. Se offline o se il push fallisce, l'entry resta in coda e viene drenata al ritorno online o all'apertura successiva dell'app. Stessa logica per i delete tramite `PendingDelete` queue.

**Last-write-wins** intra-device basato su `Entry.LastModified` (single-user, no conflict resolution). Il flag pending è tracciato esternamente in `tt-pending-sync-ids` per non sporcare il file JSON su OneDrive.

L'icona cloud nell'AppBar (`SyncIndicator.razor`) e la sezione **Sincronizzazione** in `/settings` espongono lo stato live e permettono "Sincronizza ora" + "Pulisci cache locale e ricarica da OneDrive".

## Sviluppo locale

```bash
dotnet restore
dotnet run
```

L'app si avvia su `https://localhost:5001` (o porta indicata da `Properties/launchSettings.json`). La cultura è forzata a `it-IT` in `Program.cs` (date `dd/MM/yyyy`, settimana lun→dom).

Per testare l'autenticazione Entra in locale, l'app registration deve avere registrato anche il redirect URI `https://localhost:<porta>/authentication/login-callback` come piattaforma **Single-page application**.

## Pubblicazione

Deploy automatico su GitHub Pages al push su `main` tramite `.github/workflows/deploy.yml`. Il workflow:

1. `dotnet publish -c Release` del progetto Blazor WASM
2. Patcha `<base href>` da `/` a `/TrakingTool/` (subpath di GitHub Pages)
3. Patcha `const base = "/"` in `service-worker.js` con lo stesso subpath
4. **Ricalcola la SHA-256** di `index.html` patchato e aggiorna `service-worker-assets.js`: senza questo step l'integrity check del SW fallirebbe (`SRI's integrity checks failed`)
5. Aggiunge `.nojekyll` (altrimenti GitHub Pages filtra `_framework/` e `_content/`)
6. Copia `index.html` come `404.html` per il fallback del routing client-side delle SPA
7. Carica l'artifact e lancia `actions/deploy-pages@v4`

`FORCE_JAVASCRIPT_ACTIONS_TO_NODE24=true` opt-in per zittire il warning di deprecazione Node.js 20.

## Configurazione Entra ID

`wwwroot/appsettings.json` punta a `login.microsoftonline.com/common` (multi-tenant + account personali). L'app registration in Entra deve avere:

- **Platform**: Single-page application (Authorization Code + PKCE)
- **Redirect URI**: `https://roots-insurtech.github.io/TrakingTool/authentication/login-callback`
- **Post-logout URI**: `https://roots-insurtech.github.io/TrakingTool/authentication/logout-callback`
- **API permissions** (Microsoft Graph, delegated): `User.Read`, `Files.ReadWrite.AppFolder`

## Struttura del progetto

```
TrakingTool/
├── App.razor                       Router + tema
├── Program.cs                      DI registration, cultura it-IT
├── Layout/
│   ├── MainLayout.razor            AppBar + drawer responsive (mini su desktop, overlay su mobile)
│   ├── BrandLogo.razor             SVG logo Roots Insurtech (currentColor)
│   ├── SyncIndicator.razor         Icona cloud-stato + badge pending
│   ├── LoginDisplay.razor          Menu utente Entra
│   ├── NavMenu.razor               Navigation links
│   └── RedirectToLogin.razor       Auto-login MSAL
├── Pages/
│   ├── Home.razor                  Dashboard: 4 stat card cliccabili (filtri preimpostati) + liste In corso/Aperte
│   ├── Entries.razor               Tabella attività con filtri persistiti, sort, query params, quick close
│   ├── EntryDialog.razor           Nuova/Modifica con autofocus, Ctrl+Enter, conferma su discard
│   ├── Recap.razor                 Recap mensile per cliente/area
│   ├── Import.razor                Import TSV
│   ├── Settings.razor              Stato sync + Sincronizza ora + Pulisci cache
│   └── Authentication.razor        MSAL RemoteAuthenticatorView
├── Models/
│   ├── Entry.cs                    Id, Cliente, Area, Stato, Descrizione, Data*, LastModified
│   ├── EntryYearFile.cs            Wrapper per anno (file JSON su OneDrive + LocalStorage)
│   └── Stato.cs                    Aperta/InCorso/Sospesa/Chiusa/Rilasciata/Annullata
├── Services/
│   ├── IEntryRepository.cs         Interfaccia repo
│   ├── CachedEntryRepository.cs    Cache-first, write-through, drain queue
│   ├── LocalEntryRepository.cs     LocalStorage backend (blob per anno)
│   ├── OneDriveEntryRepository.cs  Graph backend
│   ├── PendingSyncStore.cs         Code pending sync + pending delete
│   ├── SyncStateService.cs         Stato globale sync
│   ├── OnlineStatusService.cs      JS interop window.online/offline
│   ├── GraphAuthorizationMessageHandler.cs   MSAL token bearer per Graph
│   ├── StatoExtensions.cs          ToLabel(), ToCssClass()
│   └── TsvParser.cs                Import TSV
├── Theme/
│   └── NotionDarkTheme.cs          Tema MudBlazor
└── wwwroot/
    ├── index.html                  Boot screen con logo inline + favicon SVG
    ├── manifest.webmanifest        PWA manifest
    ├── service-worker.published.js Service worker (offline cache)
    ├── css/app.css                 CSS globale + media queries mobile
    ├── img/
    │   ├── roots-logo.svg          Logo full per favicon e usi futuri
    │   └── favicon.svg             Monogramma "RI" verde su sfondo bianco
    └── js/online-status.js         Bridge online/offline per OnlineStatusService
```

## Features principali

- **Offline-first**: l'app funziona senza rete; le modifiche locali vengono sincronizzate automaticamente al ritorno online
- **Indicatore di sync** in AppBar: cloud-done / sync / cloud-off / sync-problem + badge pending count
- **Filtri persistenti** in `/entries`: ricerca, stato e anno restano al refresh (`tt-entries-filters` in LocalStorage)
- **Deep link** dalla dashboard: ogni stat card è un filtro preimpostato (`?stato=Aperta`, `?month=2026-05`, ...)
- **Quick close** inline sulle attività Aperte/InCorso (icona check nella riga)
- **Date editabili** da tastierino numerico nei `MudDatePicker` (Editable=true, formato dd/MM/yyyy)
- **Dialog**: autofocus su Cliente per nuove, **Ctrl+Enter** per salvare, conferma su discard
- **Mobile-aware**: drawer overlay invece di mini, dialog con campi stack-ati verticalmente, container padding ridotto, brand subtitle nascosto

## Note operative

- **Cache PWA aggressiva**: dopo un deploy, su PWA installata può servire un giro di Application → Storage → "Cancella dati del sito" (oppure disinstalla + reinstalla) perché il vecchio service worker tenga la cache.
- **`Claude Images/`** e **`memory/`** sono in `.gitignore`: cartelle di lavoro locali per scambio di screenshot/note con assistant, non vanno committate.
- **`staticwebapp.config.json`** è un residuato di una vecchia configurazione Azure Static Web Apps; su GitHub Pages è inerte ma non disturba.
