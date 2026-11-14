# Coclico AdBlock (extension navigateur)

Extension Manifest V3, **complément navigateur** du bouclier réseau de l'app Coclico
(bloqueur niveau carte réseau : hosts + pare-feu + analyse de paquets). Ici on bloque
ce que la carte réseau ne peut pas voir : cosmétiques, bannières, et contenus
first-party (YouTube, Twitch…).

## Installation

Depuis Coclico → module « Bloqueur Télémétrie » → carte « Extension navigateur » :
choisir le navigateur → « Déployer l'extension » copie ce dossier dans
`%APPDATA%\Coclico\CoclicoAdBlock` et ouvre la page des extensions.
Puis « Mode développeur » → « Charger l'extension non empaquetée » (Chrome/Edge/Brave),
ou `about:debugging` → « Charger un module temporaire » (Firefox/Zen).

## Couches

- **DNR statiques** : `rules/core.json` (domaines + strip Cookie/Referer sur chemins de
  collecte), `rules/trackers.json` (détournement vers assets vides), `rules/aggressive.json`
  (mode max : beacons, mineurs, fingerprinting, patterns `/collect` `/adblock`…).
- **DNR dynamiques** : listes communautaires téléchargées au démarrage — Peter Lowe,
  EasyList, EasyPrivacy, EasyList FR, uBO annoyances/cookies, uBO filters complet,
  HaGeZi PRO, **Brave Specific + Brave Unbreak** (listes officielles Brave,
  `brave/adblock-lists`) — converties en règles + cosmétiques.
- **Cosmétique** : règles `##`/`#?#` des listes injectées par page (domaine + parents).
- **Scriptlets** (`scriptlets.js`, monde MAIN) : mini-moteur façon Brave/adblock-resources —
  `json-prune` (playerAds/adSlots…), `no-fetch-if` (API pub first-party), `set-constant`,
  `vaft` (Twitch), skip YouTube — pour le contenu servi par le domaine du site lui-même.
- **Cookies** : sweeper toutes les 5 min (`_ga*`, `_fbp`, `IDE`…) + strip des paramètres
  `utm_*`/`gclid`… dans les URL avant envoi.
- **Popup** : toggles (bloqueur / bouclier réseau / mode agressif / scriptlets), par-site,
  zap, compteurs, journal réseau live.

## Limites honnêtes

- MV3 : pas de webRequest bloquant ; le réseau passe par DNR (comme uBO MV3/Brave).
- Le vrai contenu TLS reste illisible sans proxy MITM : d'où la couche « carte réseau »
  dans l'app Coclico (SNI, DNS, HTTP clair, heuristiques DGA/beaconing).
