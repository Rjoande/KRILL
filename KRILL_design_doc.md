# KRILL — Design Document

**Kerbal Rebindable Inputs & Limitless groups** (backronimo confermato dall'utente, 2026-07-11).
Erede spirituale di Action Groups Extended (AGExt): estensione **trasparente** dei
10 action group stock di KSP, non un sistema parallelo. Fa coppia con KRAB-9000
(stesso ecosistema, stessa skin UI, stessa filosofia: "sembra nato col gioco").

Stato: **design chiuso in discussione con l'utente il 2026-07-11** (sessione unica,
decisioni in fondo). Nessun codice ancora scritto.

---

## 1. Visione e principi

1. **Estensione, non sostituzione.** I gruppi 11+ si comportano in tutto come i
   gruppi 1-10: stessi set, stessa semantica di attivazione, stesso gate carriera.
   Un giocatore che sa usare gli action group stock sa già usare KRILL.
2. **Una sola finestra.** Il fallimento UX di AGExt non era la finestra separata ma
   le *sei* finestre con logica arzigogolata. KRILL ha una finestra unica, pulita,
   stile KRAB (UGUI in codice, skin stock blu-grigio), aperta da toolbar.
3. **Modello stock ovunque.** Keymap globale del giocatore (come i tasti 1-0 stock),
   assegnazioni per-nave (come lo stock), set per isolare navi attraccate (come lo
   stock). Nessun concetto nuovo da imparare.
4. **HOTAS first-class.** Binding a cattura ("premi ora"), bottoni joystick come
   modificatori, nessuna lista di KeyCode da decifrare.

## 2. Fondamenta stock (verificate sul decompilato KSP 1.12.5)

### Set di action group ("Insiemi di azioni")
- Gate: `GameSettings.ADDITIONAL_ACTION_GROUPS` (impostazioni generali).
- `Vessel.GroupOverride` (0 = Predefinito, 1..4), `Vessel.SetGroupOverride(int)`,
  `Vessel.NumOverrideGroups` (= 4, static) → **5 set totali**.
- `Vessel.OverrideGroupNames[]`: nomi dei set, già per-nave, già persistiti.
- `GameEvents.OnVesselOverrideGroupChanged`: notifica cambio set (anche F6/F7
  stock via `FlightInputHandler` → `ActionGroupsApp.SelectPrev/SelectNext`).
- **Assegnazioni per-set sulle azioni**: `BaseAction.actionGroup` (bitmask, set
  Predefinito) + `BaseAction.overrideGroups[4]` (set 1..4);
  `BaseAction.GetActionGroup(int groupOverride)` risolve la maschera del set
  attivo. Persistiti da stock come `actionGroup` e `overrideGroup0..3` nel nodo
  ACTIONS di ogni parte. **KRILL replica questa identica forma per i gruppi 11+.**
  (Gli assi hanno lo specchio esatto in `BaseAxisField` — territorio KRAB.)

### Binding tastiera stock
- AG1-10: `GameSettings.CustomActionGroup1..10` (KeyBinding; confermati anche in
  `settings.cfg`). Editabili da codice + `GameSettings.SaveSettings()` → la UI di
  KRILL può gestire anche i bind 1-10, scrivendo quelli stock (coerenza totale:
  le impostazioni stock riflettono le modifiche).

### Carriera
- I gruppi custom stock si sbloccano con VAB/SPH al livello massimo (Tier 3).
  KRILL riusa **lo stesso check dello stock** per i gruppi 11+ (nessuna logica
  propria; da individuare il punto esatto in `GameVariables` in M1).

### Perché i gruppi 11+ non possono essere KSPActionGroup veri
`KSPActionGroup` è un enum bitmask a 32 bit già pieno: non estendibile. I gruppi
11+ sono quindi "virtuali": storage e attivazione nostri (invocazione diretta
delle `BaseAction`), ma con forma dati e semantica identiche allo stock.

## 3. Modello dati

- **Gruppo**: indice intero. 1-10 = gruppi stock (delega totale allo stock);
  11+ = gruppi KRILL. Modello **sparso**: un gruppo 11+ esiste solo se ha almeno
  un'azione assegnata o un nome. Nessun array fisso in stile AGExt-250.
- **Tetto visibile**: configurabile dalla pagina impostazioni stock
  (`GameParameters.CustomParameterNode`, sezione KRILL). Default 20 totali,
  espandibile con bottone "+" nella UI.
- **Assegnazione**: `(set 0..4, gruppo, parte, azione)` — simmetria set×gruppo
  completa, identica ai gruppi stock.
- **Identità azione** (persistenza robusta, lezione AGExt): la coppia
  `persistentId` della parte + `moduleName` + `actionName`. **MAI** l'indice
  posizionale del modulo (`pmIndex`, il difetto di AGExt che perde le azioni a
  ogni update di mod che riordina i moduli).
- **Nomi gruppo**: per-nave, tabella `(set, gruppo) → nome` con **ereditarietà dal
  set Predefinito** (un nome definito nel set 0 vale per tutti i set finché non
  overridato). Valgono anche per i gruppi 1-10 (visibili nella UI KRILL; nella UI
  stock solo dopo l'eventuale fase di iniezione).

## 4. Attivazione e binding

- **Keymap globale del giocatore**: `gruppo → (KeyCode principale, modificatori)`.
  Vale per tutte le navi e tutti i set (come i tasti 1-0 stock: il set attivo
  decide *cosa* scatta, non *quale tasto*). NON-GOAL: bind per-nave.
- **Cattura** ("premi ora il tasto"): durante la cattura si scandiscono tutti i
  KeyCode Unity (tastiera + `Joystick1Button0..Joystick8Button19`); il primo
  evento registra il bind. Esc/timeout annulla. I modificatori sono i tasti/bottoni
  tenuti premuti al momento della cattura (anche bottoni joystick: un pinky shift
  HOTAS è un layer).
- **Conflitti**: avviso non bloccante, con **indicazione di cosa è già assegnato**,
  controllando sia la keymap KRILL sia i KeyBinding stock di `GameSettings`
  (es.: "Il tasto 'R' è già in uso (RCS)!"). Come lo stock: avvisa, non vieta.
- **Gruppi 1-10**: la UI KRILL mostra e modifica i bind stock
  (`GameSettings.CustomActionGroup1..10` + `SaveSettings()`); l'attivazione resta
  interamente stock. I modificatori estesi (layer joystick) valgono solo per i
  gruppi 11+ nel nucleo; l'eventuale "presa in carico" dei 1-10 per dare anche a
  loro i layer è un'estensione futura documentata, non nel nucleo.
- **Vincolo HOTAS**: l'input legacy Unity vede max **20 bottoni per joystick**
  (8 device). VKB dell'utente = 2 device indipendenti (stick + throttle) → 40
  bottoni visibili; il software di mappatura VKB copre il resto. Lettura HID raw
  = fuori dal nucleo (eventuale milestone futura, porta dipendenze).

## 5. Persistenza — tre scope, tre posti

| Dato | Scope | Dove | Come |
|---|---|---|---|
| Assegnazioni azioni→(set,gruppo) | craft/nave | mini-PartModule per-parte (MM `@PART[*]`) | nodi ACTION con persistentId+moduleName+actionName; forma speculare a `overrideGroup0..3` stock |
| Nomi gruppi, metadati per-nave | save | i moduli delle parti (in volo, via nave) + blob sul modulo per il craft | round-trip ConfigNode completo |
| Keymap del giocatore | globale | `PluginData/keymap.cfg` | sopravvive ai verify Steam, non trigghera il rebuild MM |

- **Lezione KRAB obbligatoria**: le istanze editor sono cloni Unity, `OnLoad` non
  gira sul clone → ogni dato complesso del modulo ha un backup `[SerializeField]
  string` (ConfigNode.ToString/Parse) tenuto aggiornato a ogni mutazione.
- Il mini-modulo per-parte è l'unico compromesso "invasivo" (inevitabile per far
  viaggiare le assegnazioni col craft file, come dimostra lo stesso stock che salva
  nelle ACTIONS delle parti). Dev'essere minimale: un nodo per azione assegnata,
  niente placeholder "Hello World" alla AGExt su parti senza dati.

## 6. UI

- **Nucleo (M3)**: finestra unica UGUI in codice, skin KRAB (stock blu-grigio),
  aperta da ToolbarControl (icona propria). Due contesti:
  - **Editor**: lista gruppi (1-10 + 11+ uniformi) × tab set; selezione parti col
    meccanismo di evidenziazione stock (il codice del PR AGExt: hover ciano/rosso);
    assegnazione azioni per parte selezionata.
  - **Volo**: stessa finestra in modalità trigger/gestione (lista gruppi cliccabile,
    rinomina, riassegnazione bind).
- **Keymap editor**: sezione della stessa finestra stile impostazioni stock
  (riga per gruppo: nome, bind attuale, bottone "cattura", avviso conflitti).
- **Unificazione AGSetHUD — CHIUSA (2026-07-27)**: non un porting di codice,
  una sostituzione funzionale. La finestra KRILL (tab dei 5 set + riga
  salto-set diretto, M4) copre già per intero quello che offriva lo switcher
  standalone; l'utente ha rimosso il vecchio mod dall'installazione. Nessun
  widget da riscrivere.
- **Gestione dei gruppi stock 1-10 nella UI di KRILL — pianificata
  (2026-07-27, rovescia il non-goal deciso il 2026-07-11, vedi §11)**:
  KRILL gestirà anche l'ASSEGNAZIONE di azioni ai gruppi 1-10 dalla propria
  finestra, non solo nome e bind. Non ancora progettata nel dettaglio (resta
  da capire se riusare lo stesso meccanismo "+ Part"/"+ Action" delle colonne
  2/3, oggi disabilitato per i gruppi stock, o qualcosa di dedicato).
- **Fase pubblicazione (M5+, documentata ora, sviluppata poi)**:
  - pulsantiera HUD di volo (griglia bottoni grandi cliccabili);
  - ~~iniezione nella UI stock~~ **scartata (2026-07-27)**, vedi §9.

## 7. Localizzazione (standard KRAB/T4S)

- Ogni stringa giocatore passa da `#LOC_KRILL_*`; master inglese
  (`Localization/en-us.cfg`); niente stringhe hardcoded nella UI.
- **Riciclo chiavi stock dove possibile** (zero traduzioni da mantenere): es.
  `#autoLOC_6013000` ("Predefinito"), `#autoLOC_6013001` ("Impostaz. <<1>>"),
  `#autoLOC_6013003` (nome feature set) — già censite in AGSetHUD; i nomi delle
  azioni e dei gruppi stock arrivano già localizzati dal gioco.
- **Ogni riga del file di localizzazione ha un commento-spiegazione** (contesto
  d'uso, vincoli di lunghezza) per facilitare i traduttori.
- Commenti nel codice e nei .cfg: inglese. Chat e note interne: italiano.

## 8. Sviluppo incrementale (stop di test guidato a ogni milestone)

Ogni milestone termina con build pulita + DLL deployata + **checklist di test in
gioco scritta in `notes/`** prima di procedere (protocollo KRAB/T4S). Architettura
per file piccoli e responsabilità chiare, documentazione puntuale nei summary —
il progetto deve restare lavorabile anche da modelli meno capaci (Sonnet 5).

- **M1 — Data model e persistenza**: gruppi sparsi, assegnazioni (set×gruppo),
  nomi, mini-modulo per-parte con round-trip completo e backup SerializeField;
  self-test in-game via PAW dietro `debugMode` cfg-only (pattern KRAB).
  *Test: craft round-trip, simmetrie, quicksave/load, docking/undocking.*
- **M2 — Motore di attivazione**: keymap globale (PluginData), cattura, conflitti
  con dettaglio, gate carriera, reattività al cambio set, invocazione azioni 11+.
  *Test: bind tastiera+VKB (primo censimento reale di cosa espone il VKB a Unity),
  layer con modificatore joystick, F6/F7, carriera Tier<3.*
- **M3 — UI unica**: finestra editor+volo, evidenziazione parti stock-style,
  keymap editor, gestione bind 1-10 via GameSettings.
  *Test: flusso completo "creo gruppo 14, assegno, rinomino, bindo, attivo".*
- **M4 — Rifinitura**: unificazione AGSetHUD, polish UX, pass di localizzazione.
  **Avviata 2026-07-23** con la prima parte: **tasto di salto diretto a un
  set** (es. Keypad3 → passa subito al set 3, indipendentemente dal set
  corrente), proposto dall'utente 2026-07-11, rimandato di proposito rispetto
  al resto del polish M3. Implementata con un secondo keymap globale
  SEPARATO (`KrillSetKeymap`, `PluginData/setkeymap.cfg`) — assegnabile in
  entrambe le scene come i bind gruppo, attivabile solo in volo (richiede una
  Vessel reale). Vedi §11 log 2026-07-23 per il dettaglio della decisione.
  **M4 chiusa 2026-07-27**: unificazione AGSetHUD risolta per sostituzione
  (§6), pass di localizzazione fatto (inglese+italiano, parità completa).
  Aggiunta alla coda: gestione dei gruppi 1-10 nella UI di KRILL (§6, §9).
- **M5+ (solo se si pubblica)**: pulsantiera HUD. Traduzioni oltre
  inglese/italiano non pianificate. Iniezione UI stock **scartata**, vedi §9.

## 9. Non-goal (decisi, non riaprire senza motivo)

- Bind per-nave (il modello stock non li ha; complicavano AGExt).
- Keyset custom alla AGExt (sostituiti dai 5 set stock).
- 250 gruppi fissi (modello sparso + tetto configurabile).
- RemoteTech, kOS, controllo OtherVessel, OSK, API esterna per altre mod
  (riaperti eventualmente solo in fase pubblicazione, su richiesta reale).
- Lettura HID raw nel nucleo.
- **Iniezione nella UI stock** (schermata Azioni editor / app Action Groups
  volo) — proposta come possibile fase M5+, **scartata (2026-07-27)**: al suo
  posto si è deciso di gestire i gruppi 1-10 direttamente nella UI di KRILL
  (§6, §8), rendendo l'iniezione superflua.

~~Picker di assegnazione KRILL per i gruppi stock 1-10~~ — **non più un
non-goal**: deciso il 2026-07-11 (le due UI restavano separate), **rovesciato
il 2026-07-27** — vedi §6/§8/§11. Tenuto qui depennato invece che cancellato,
per lo storico della decisione.

## 10. Punti aperti residui

1. ~~Backronimo~~ **CHIUSO (2026-07-11)**: "Kerbal Rebindable Inputs & Limitless groups".
2. ~~Check carriera~~ **CHIUSO (verificato sul decompilato, avvio M1)**:
   `GameVariables.Instance.UnlockedActionGroupsCustom(editorNormLevel, isVAB)` —
   true se `AdvancedParams.ActionGroupsAlways`, altrimenti `editorNormLevel > 0.6`
   (Tier 3). KRILL delega a questo stesso metodo (virtuale → rispetta eventuali
   override di altre mod): `KrillQuery.ExtendedGroupsUnlocked`.
3. ~~Fallback di `GetActionGroup`~~ **CHIUSO (verificato, avvio M1)**: NESSUN
   fallback — nei set 1..4 conta solo `overrideGroups[i]`; se è None l'azione non
   appartiene ad alcun gruppo in quel set. **I set sono indipendenti; il set 0 non
   viene ereditato.** KRILL replica questa semantica per le assegnazioni. (I *nomi*
   gruppo KRILL invece ereditano dal set 0 — regola di sola visualizzazione, §3.)
4. Cosa espone esattamente il VKB a Unity (device × bottoni) — primo test in M2.

## 11. Decisioni utente (log)

- 2026-07-11: visione "estensione trasparente dello stock"; 250 gruppi = troppi,
  tetto configurabile; keyset eliminati in favore dei 5 set stock; simmetria
  set×gruppo confermata; iniezione UI stock e pulsantiera HUD rimandate a fase
  pubblicazione (architettura predisposta); career gate = stock Tier 3; UI dedicata
  pulita stile "standard mod" ma su stack UGUI/skin KRAB; nome **KRILL** (scartato
  "Moar Action Groups" per collisione con vecchia mod); keymap globale del
  giocatore + cattura "premi ora" (idea utente, complementari); avvisi di conflitto
  con dettaglio dell'assegnazione esistente inclusi i bind stock; nomi gruppo
  personalizzabili anche per 1-10; gestione bind 1-10 dalla UI KRILL via
  GameSettings; unificazione futura di AGSetHUD.
- 2026-07-11 (post-test M3): Assign e Trigger disponibili in **entrambe** le
  scene per i gruppi estesi (non più editor/volo separati); click su un tab
  set in volo **cambia davvero** `Vessel.GroupOverride` (prima era solo un
  filtro locale della UI, bug corretto); vista dettaglio per gruppo (espansione
  in linea nella lista, evidenziazione blu scuro sulle parti assegnate, [x]
  rosso per rimuovere una singola assegnazione); "+ New Group" crea una riga
  vuota con nome placeholder **senza** aprire il picker, calcolando il prossimo
  numero libero sul **keymap globale** (non solo sulle assegnazioni della nave
  corrente, per non riusare per sbaglio un numero già significativo altrove);
  rimozione gruppo (solo l'ultimo visualizzato, conferma a due click, svuota
  assegnazioni/nomi su tutte le parti ma non il bind globale). Rimandato a M4:
  tasto di salto diretto a un set (§8). Scartato: picker KRILL per gruppi
  stock 1-10 (§9) — le due UI restano separate.
- 2026-07-20 (post-test layout 3 colonne): **confermato, non riaprire senza
  motivo** — la rinomina dei set 1-4 resta **solo in volo**. Verificato sul
  decompilato (`Vessel.cs`, costruttore) che `OverrideGroupNames` è
  un'allocazione per-istanza del `Vessel`: non esiste alcun campo stock
  equivalente su `ShipConstruct`/`EditorLogic` prima del lancio, quindi
  supportarlo in editor richiederebbe un meccanismo KRILL-only (nome
  provvisorio + copia al lancio), che romperebbe il principio "simmetrico con
  stock" già deciso per questa stessa feature (2026-07-18). Utente ha
  scelto esplicitamente di non introdurlo.
- 2026-07-23 (avvio M4, salto diretto a un set): chiarito che il "probabilmente
  solo VAB" di §8 si riferiva a dove ASSEGNARE il tasto, non a dove funziona —
  con la keymap globale dedicata (non più legata a una nave/craft) non c'è
  motivo di limitare l'assegnazione a una scena, esattamente come i bind
  gruppo. Confermato **keymap separata** (non condivisa col dizionario dei
  bind gruppo): stesso spazio di piccoli interi (set 0-4 vs gruppo 11+),
  mischiarli avrebbe introdotto un'ambiguità di significato per risparmiare un
  file. L'attivazione resta di sola volo (`Vessel.SetGroupOverride` richiede
  una Vessel), confermato dall'utente.
- 2026-07-27 (chiusura M4, checklist `test-m3-3col.md`/`test-m4-setjump.md`
  entrambe superate per intero): **due decisioni**. (1) **Unificazione
  AGSetHUD chiusa per sostituzione, non per porting**: l'utente ha rimosso il
  vecchio mod standalone dall'installazione — la finestra KRILL (tab set +
  riga salto-set) ne riproduce già pienamente la funzione, nessun codice da
  assorbire. (2) **Rovesciato il non-goal del 2026-07-11** "picker KRILL per
  gruppi stock 1-10": invece dell'iniezione nella UI stock (proposta per
  M5+), si pianifica di gestire l'ASSEGNAZIONE dei gruppi 1-10 direttamente
  nella UI di KRILL — l'iniezione nella UI stock passa da "possibile M5+" a
  **scartata** (§9), resa superflua da questa scelta. Non ancora progettato
  il meccanismo concreto (dettaglio in §6/§8).
