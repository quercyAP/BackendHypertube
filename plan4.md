## Plan 4 – Démo MediaSource / MSE (bonus)

Objectif : ajouter une **petite démo ciblée** de streaming via MediaSource (MSE), en s’appuyant uniquement sur les MP4 déjà produits par le pipeline de `plan3` (H.264 + AAC en conteneur MP4). Pas de branchement direct sur le torrent en temps réel.

---

### 1. Objectifs clairs pour la soutenance

- **Montrer** qu’on sait alimenter un `<video>` via **MediaSource** avec des segments binaires fournis par le backend.
- S’appuyer sur un **fichier MP4 déjà finalisé** (remux ou transcodage audio) pour ne pas complexifier le torrent/download manager.
- Garder la démo **indépendante** du player principal : une page ou section séparée, clairement marquée comme "bonus".

Résultat attendu :

- Une page de test (par ex. `/mse-test.html` ou une section dédiée dans `index.html`) où :
  - on sélectionne un film déjà converti (ex : `Pirates of Silicon Valley`),
  - on clique sur "Start MSE demo",
  - le `<video>` est alimenté par des segments via MediaSource, et la vidéo se lit normalement.

---

### 2. Choix techniques simplifiés

#### 2.1. Format côté backend

- Source : MP4 **déjà produit** par `StartConversionIfNeededAsync` (H.264 + AAC, `*_remuxed.mp4` ou `*_audioaac.mp4`).
- Pas de ré-encodage : uniquement **lecture** d’un fichier existant.
- Découpage en segments **simples** :
  - soit par **taille fixe** (par ex. 1 Mo par segment),
  - soit par une granularité un peu plus fine (256–512 Ko) si besoin.
- On accepte que ce ne soit pas parfaitement aligné sur les keyframes → pour la démo, ce n’est pas bloquant.

#### 2.2. API côté navigateur

- Utiliser **MediaSource** (MSE) :
  - création d’un objet `MediaSource`,
  - attachement à `<video>` via `URL.createObjectURL(mediaSource)`,
  - création d’un `SourceBuffer` avec un MIME type compatible avec notre MP4 H.264+AAC.
- Boucle de chargement de segments :
  - fetch successifs de `/api/dev/segments/{movieId}/{index}`,
  - alimentation du `SourceBuffer` jusqu’à la fin du fichier.

---

### 3. Backend – Design des endpoints (NoCode)

#### 3.1. Identifier le MP4 converti à partir d’un `torrentId`

- Réutiliser l’existant : `TorrentDownloadService` sait déjà :
  - où est stocké le fichier final (`ConvertedFilePath` ou `PersistedFilePath`),
  - si le torrent est `Seeding` / prêt.
- Créer une méthode de service de haut niveau (pseudo) :
  - "`GetFinalVideoPathForMseAsync(Guid torrentId)`" qui :
    - vérifie que le torrent existe,
    - vérifie que `ConvertedFilePath` pointe vers un `.mp4` H.264+AAC,
    - retourne ce chemin ou une erreur claire (`409` si pas encore prêt, `404` si introuvable).

#### 3.2. Endpoint segments – `/api/dev/segments/{torrentId}/{index}`

- **Méthode** : `GET`.
- Paramètres :
  - `torrentId` : GUID du torrent déjà converti.
  - `index` : index de segment (entier ≥ 0).
- Comportement :
  1. Récupérer le chemin du MP4 final via le service décrit en 3.1.
  2. Définir une **taille de segment** (par ex. `SegmentSizeBytes = 1_000_000`).
  3. Calculer l’offset : `offset = index * SegmentSizeBytes`.
  4. Si `offset >= fileLength` :
     - retourner `204 No Content` ou `404` pour signaler qu’il n’y a plus de segments.
  5. Calculer `length = min(SegmentSizeBytes, fileLength - offset)`.
  6. Lire les `length` octets à partir de `offset` et renvoyer :
     - statut `200`,
     - corps binaire,
     - `Content-Type: video/mp4` (ou `application/octet-stream`),
     - éventuellement un header custom (`X-Total-Length`, `X-Segment-Size`, etc.).

- Gestion des erreurs :
  - torrent introuvable → `404` avec message JSON.
  - fichier pas encore prêt (pas de MP4 final) → `409 Conflict` avec message "Video not ready for MSE".
  - index négatif / invalide → `400 Bad Request`.

> Remarque : ce endpoint est **pure démo** (namespace `/api/dev/...` possible) pour bien séparer du flux principal `/api/torrents/{id}/stream`.

---

### 4. Frontend – Page / section de test MSE (NoCode)

#### 4.1. Emplacement

Deux options acceptables :

- **Option A** – nouvelle page statique `mse-test.html` dans `wwwroot` :
  - claire pour le jury : "cette page est notre démo MediaSource".
- **Option B** – section supplémentaire dans `index.html` :
  - un onglet ou un bloc "MSE Demo" sous le player minimal.

Pour la clarté de la soutenance, l’**Option A** (page dédiée) est préférable.

#### 4.2. Comportement de la page de test

- **Entrées utilisateur minimales** :
  - un champ texte ou select pour choisir un `torrentId` (ou un film prédéfini : Pirates / Amélie),
  - un bouton "Start MSE demo".

- **Étapes côté JS (logique non codée, seulement décrite)** :
  1. Créer un `MediaSource` et l’attacher au `<video>`.
  2. Sur `sourceopen`, créer un `SourceBuffer` avec le bon MIME H.264+AAC.
  3. Boucler sur les index de segments : 0, 1, 2, ... :
     - faire des requêtes GET successives vers `/api/dev/segments/{torrentId}/{index}`.
     - arrêter quand le serveur renvoie `204/404`.
  4. Pousser chaque segment dans le `SourceBuffer` dès qu’il est disponible.
  5. Gérer un indicateur d’état dans l’UI :
     - "Fetching segment N...", puis "All segments loaded".

- **Cas d’erreur / edge cases** :
  - si `/api/dev/segments/...` renvoie `409` (vidéo pas prête) → afficher un message type :
    - "La vidéo n’est pas encore convertie en MP4, lancer d’abord le téléchargement classique".
  - si `404` dès le segment 0 → torrentId invalide ou fichier manquant → message d’erreur lisible.

---

### 5. Scénario de démo pour la soutenance

#### 5.1. Préparation

1. Utiliser le pipeline de `plan3` pour préparer un MP4 final sur un film simple (ex. `Pirates of Silicon Valley` ou `Amélie`).
2. Vérifier via `/api/torrents` que :
   - le torrent est en `Seeding`,
   - `FilePath` ou `ConvertedFilePath` pointe vers un `.mp4`.

#### 5.2. Déroulé de la démo MSE

1. Ouvrir d’abord le player minimal (`index.html`) :
   - montrer le flux classique `<video src="/api/torrents/{id}/stream">` pour ancrer le fonctionnement de base.
2. Ouvrir ensuite la page de test MSE (`mse-test.html`) :
   - saisir ou sélectionner le même `torrentId` utilisé précédemment.
   - cliquer sur "Start MSE demo".
3. Commenter à l’oral :
   - "Ici, la vidéo n’est plus lue via un simple `src` sur un fichier, mais via MediaSource :
      le navigateur crée un buffer interne et on lui pousse nos propres segments depuis `/api/dev/segments/...`".
   - "Le backend pourrait à terme alimenter ces segments directement depuis le torrent via ffmpeg, mais pour rester raisonnable dans le projet, on se base sur le MP4 final déjà produit.".
4. Montrer que la vidéo se lit correctement, avec :
   - au moins play/pause,
   - et idéalement un petit retour visuel sur la progression du chargement des segments.

---

### 6. Limites assumées (à expliquer)

- Les segments sont découpés de manière naïve (par taille fixe) et pas forcément alignés sur les keyframes → suffisant pour une **démo pédagogique**, pas pour de la prod.
- Le lien avec le torrent est **indirect** :
  - torrent → MP4 final (pipeline de `plan3`),
  - MP4 final → segments → MediaSource.
- Le design complet "torrent → ffmpeg en pipe → segments → MediaSource" est documenté mais **pas implémenté**, pour éviter une explosion de complexité (gestion temps réel, erreurs, charge CPU).

Ces limites sont des arguments à utiliser en soutenance pour montrer que le scope est maîtrisé et justifié.

