## Plan 6 – Player MSE fonctionnel avec libs (post-torrent)

Objectif : concevoir un **player MSE pleinement fonctionnel** en s’appuyant sur des bibliothèques dédiées au packaging/lecture vidéo, tout en respectant la contrainte du sujet :

- **Aucun usage de libs "torrent → stream"** (webtorrent, pulsar, peerflix, etc.).
- On part d’un **fichier MP4 H.264+AAC déjà obtenu** par notre pipeline torrent + conversion (plans 3/4/5).

L’idée :

> "Le pipeline torrent → fichier MP4 → conversion/remux est 100% maison. 
>  Pour la partie très spécifique MSE (init segment, fMP4, buffering, erreurs), on s’appuie sur des libs standard du monde vidéo."

---

### 1. Rappel du pipeline actuel (point de départ)

- **Téléchargement** :
  - Torrent géré en C# (BitTorrent maison),
  - fichiers téléchargés sur disque,
  - implémentation : `TorrentDownloadService` + `ITorrentDownloadService` (stockage des téléchargements actifs et suivi des progrès).

- **Post-traitement vidéo** (plan3) :
  - Détection des codecs via ffprobe/`IVideoCodecDetector` + `VideoCodecDetector`,
  - remux/transcodage audio via `IVideoRemuxService`/`VideoRemuxService` et `IVideoConversionService`,
  - production d’un **MP4 H.264+AAC** compatible web stocké sur disque,
  - MP4 final exposé via `/api/torrents/{id}/stream` (player classique ASP.NET Core).

- **Tentatives MSE maison** (plans 4/5) :
  - segmentation naïve (taille fixe) → MP4 tronqué → non lisible,
  - segmentation avancée via ffmpeg (`-f segment`) orchestrée par `MseStreamingService` → segments MP4 consommés par MSE JS mais non jouables (profil fMP4 non conforme).

Conclusion :

- La partie pédagogique "torrent → MP4" est implémentée.
- La partie "packaging fMP4 exact pour MSE" est très complexe si faite maison.

---

### 2. Positionnement des libs autorisées

Le sujet interdit les libs qui font :

> *"create a video stream from a torrent"* (webtorrent, pulsar, peerflix, etc.).

Mais nous :

- avons déjà **le MP4 sur disque**,
- voulons uniquement améliorer **l’emballage/lecture** côté vidéo.

Donc **autorisés** :

- **Côté backend** :
  - Outils de packaging HLS/DASH ou MP4 :
    - ex : MP4Box/GPAC, packagers HLS/DASH en ligne de commande.
  - On les appelle comme ffmpeg : processus externes, pas de lien avec le torrent.

- **Côté frontend** :
  - Players MSE/HLS/DASH type :
    - `hls.js`, `dash.js`, `shaka-player`, etc.
  - Ils ne gèrent que le flux HTTP (fichiers/segments), pas le torrent.

Idée clé :

> "On implémente nous-mêmes tout ce qui est avant le MP4, puis on utilise des libs reconnues pour s’interfacer proprement avec MSE."

---

### 2.1. Vérification des références code existant

| Élément mentionné | Situation actuelle | Notes |
| --- | --- | --- |
| `ITorrentDownloadService.GetFinalVideoPathForMseAsync` | Implémenté dans `TorrentDownloadService` (retourne un MP4 H.264+AAC prêt pour MSE). | Utilisé par `MseStreamingService`. |
| Détection/remux (`IVideoCodecDetector`, `IVideoRemuxService`) | Implémentés et enregistrés dans `Program.cs`. | Utilisés lors du post-traitement plan3. |
| Endpoint `/api/torrents/{id}/stream` | Existant dans `TorrentsController`. | Sert le MP4 final avec support Range. |
| Player MSE de test (`mse-test.html`) | **N’existe plus dans le repo**. | Plan6 doit prévoir une nouvelle page front-end dédiée HLS/MSE. |
| Service HLS (`IHlsPackagingService`) | À créer. | Aucun équivalent actuel. |

Tous les points du plan qui référencent du code existant ont été vérifiés et mis à jour dans la table ci-dessus. La seule référence obsolète concernait la page `mse-test.html`.

---

### 3. Stratégie haut niveau du plan6

#### 3.1. Option A – Packaging HLS (simple et compatible libs)

- **Backend** :
  - À partir du MP4 final, générer une arborescence HLS :
    - un manifest `index.m3u8`,
    - des segments `.ts` ou `.m4s` (fMP4),
  - Servir ces fichiers en statique ou via un mini contrôleur.

- **Frontend** :
  - Utiliser `hls.js` dans le navigateur :
    - créer un `video` + `MediaSource`,
    - `hls.js` télécharge `index.m3u8` + segments, remplit MSE tout seul.

Avantages :

- Format **très standard**, bien supporté.
- `hls.js` gère : buffering, erreurs réseau, MSE, etc.
- On garde notre API torrent et notre player classique en parallèle.

Inconvénients :

- Ajout d’un packager HLS côté backend.

#### 3.2. Option B – Packaging DASH

- Même principe que HLS, mais avec manifest MPD + segments fMP4.
- Utilisation d’un player type `dash.js` côté frontend.

Option A (HLS) est en général plus simple à mettre en place et bien supportée (notamment par `hls.js`).

---

### 4. Design concret – Option A (HLS + hls.js)

#### 4.1. Backend : génération HLS à partir du MP4 final

Point d’entrée : MP4 final (H.264+AAC) obtenu via `GetFinalVideoPathForMseAsync(Guid torrentId)`.

1. **Service de packaging HLS** (nouveau) – `IHlsPackagingService` / `HlsPackagingService` :

   - Responsabilités :
     - `Task<string?> GetOrCreateHlsPlaylistForTorrentAsync(Guid torrentId, CancellationToken ct)` :
       - vérifie l’existence du MP4 final,
       - si la playlist HLS existe déjà pour ce torrent (en cache/disque), renvoie son chemin/URL,
       - sinon :
         1. crée un dossier `hls/{torrentId}/`,
         2. lance un packager (ffmpeg ou outil dédié) pour générer :
            - `index.m3u8`,
            - segments `.ts` ou `.m4s`,
         3. stocke le chemin de la playlist,
         4. renvoie l’URL relative `/hls/{torrentId}/index.m3u8`.

   - Implémentation possible avec **ffmpeg** (pour rester dans ce qui est déjà utilisé) :

     ```bash
     ffmpeg -i input.mp4 \
       -map 0:v:0 -map 0:a:0? \
       -codec: copy \
       -start_number 0 \
       -hls_time 4 \
       -hls_segment_filename "hls/{torrentId}/seg_%05d.ts" \
       "hls/{torrentId}/index.m3u8"
     ```

     (on pourra affiner selon les capacités du navigateur/`hls.js`, et documenter que ce packaging est fait **après** la phase torrent.)

2. **Contrôleur HLS** (minimal) – ou simple exposition statique :

   - Option simple :
     - générer les fichiers dans `wwwroot/hls/{torrentId}/...`
     - la playlist est alors accessible via `http://host/hls/{torrentId}/index.m3u8`.

   - Option API explicite :
     - `GET /api/mse/hls/{torrentId}` qui :
       - appelle `GetOrCreateHlsPlaylistForTorrentAsync`,
       - renvoie `{ playlistUrl: "/hls/{torrentId}/index.m3u8" }`.

3. **Intégration avec le reste** :

   - On garde :
     - `/api/torrents/{id}/stream` pour le player classique,
     - `/api/torrents/{id}/progress` pour l’état du torrent.
   - Plan6 ajoute simplement une voie parallèle MSE/HLS.

#### 4.2. Frontend : player HLS basé sur hls.js

Créer **une nouvelle page** (ex. `hls-player.html`) — aucun prototype MSE n’existe aujourd’hui :

- UI :
  - champ JWT (réutiliser la logique de `index.html`),
  - liste des torrents (appel `/api/torrents`),
  - bouton "Play with HLS/MSE" à côté du player classique.

- JS :
  1. Lors d’un clic sur "Play with HLS" :
     - récupérer `torrentId`,
     - appeler éventuellement `/api/torrents/{id}/progress` pour vérifier que le MP4 final est prêt,
     - appeler `GET /api/mse/hls/{torrentId}` (si on a choisi l’API explicite) ou construire l’URL `/hls/{torrentId}/index.m3u8`.
  2. Créer un `<video>` et attacher `hls.js` :

     ```js
     if (Hls.isSupported()) {
       const hls = new Hls();
       hls.loadSource(playlistUrl);
       hls.attachMedia(videoElement);
       hls.on(Hls.Events.MANIFEST_PARSED, () => {
         videoElement.play().catch(() => {});
       });
     } else if (videoElement.canPlayType('application/vnd.apple.mpegurl')) {
       videoElement.src = playlistUrl;
       videoElement.play().catch(() => {});
     }
     ```

- Sécurité / token :
  - soit les fichiers HLS sont servis en statique (pas protégés → à argumenter),
  - soit le dossier `hls` n’est pas sous `wwwroot`, et on expose les segments via un contrôleur protégé, avec `Authorization: Bearer` (plus complexe, mais plus cohérent avec le reste de l’API).

---

### 5. Roadmap d’implémentation pour plan6

1. **Backend**
   - [ ] Créer `IHlsPackagingService` + `HlsPackagingService` utilisant ffmpeg pour générer `index.m3u8` + segments.
   - [ ] Ajouter la logique de cache : ne pas re-générer si la playlist existe déjà pour un `torrentId`.
   - [ ] Ajouter un mécanisme de nettoyage (optionnel) pour les anciens dossiers HLS.
   - [ ] Exposer les fichiers HLS :
     - soit via `wwwroot/hls/{torrentId}` (simple),
     - soit via un contrôleur dédié avec auth.
   - [ ] Ajouter des logs/metrics pour suivre la génération HLS (durée, erreurs ffmpeg, taille des segments).

2. **Frontend**
   - [ ] Ajouter `hls.js` (CDN ou bundler) sur une nouvelle page `hls-player.html`.
   - [ ] Réutiliser la liste des torrents (`/api/torrents`) pour choisir un `torrentId`.
   - [ ] Sur clic "Play with HLS", récupérer l’URL de playlist HLS et initialiser `hls.js`.
   - [ ] Afficher clairement l’état : `Preparing HLS`, `Buffering`, `Playing`, `Error`.
   - [ ] Ajouter des contrôles (reload, clear cache) pour faciliter les tests pendant la soutenance.

3. **Démo et soutenance**
   - [ ] Scénario de démo :
     1. Montrer le player classique (HTTP Range direct) pendant le download.
     2. Montrer le player HLS/MSE (après que le MP4 soit prêt) en expliquant l’usage de `hls.js`.
   - [ ] Expliquer la différence entre :
     - pipeline maison (plans 3/4/5),
     - packaging/lecture HLS avec libs (plan6).
   - [ ] Mettre en avant que l’interdiction du sujet porte sur "torrent → stream", pas sur "MP4 → HLS/MSE".

---

Ce plan6 est conçu pour :

- **respecter le sujet** (toute la partie torrent + conversion est déjà faite maison),
- **obtenir un player MSE réellement fonctionnel** grâce à des libs éprouvées (hls.js + ffmpeg packaging HLS),
- **montrer une montée en complexité maîtrisée** :
  - plan3 : pipeline torrent → MP4,
  - plan4/5 : explorations MSE maison (limites expliquées),
  - plan6 : solution "industrielle" en s’appuyant sur des outils standard du domaine vidéo.
