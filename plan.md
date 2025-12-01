# Plan de travail BackendHypertube

## 1. Persistance simple des torrents

### 1.1. Objectifs

- Garder la liste des torrents téléchargés après un redémarrage de l’API.
- Pouvoir les exposer via les endpoints existants (`/api/torrents`, `/progress`, `/stream`).
- Ne pas (encore) se préoccuper du re-seeding complet au reboot, seulement de la visibilité et du streaming.

### 1.2. Modèle de métadonnées

- Créer un format JSON par torrent, par exemple :
  - Dossier : `DOWNLOAD_DIRECTORY` (déjà utilisé, ex. `downloads/`).
  - Fichier : `downloads/<torrentId>.json`.
- Contenu typique :
  - `torrentId` (GUID)
  - `movieTitle`
  - `infoHash` (hex)
  - `downloadPath` (dossier racine du torrent sur disque)
  - `filePath` (chemin final utilisé pour le streaming)
  - `status` (`Completed`, `Seeding`, `Error`, `Cancelled`, etc.)
  - `startedAt`, `completedAt` (timestamps)
  - `bytesDownloaded` (optionnel)
  - `originalTorrentUrl` (optionnel, pour debug)

### 1.3. Écriture du JSON en fin de téléchargement

- Point d’accroche : dans [TorrentDownloadService.StartDownloadAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:38:4-126:5), dans la tâche qui suit [downloadManager.StartDownloadAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:38:4-126:5) :
  - **Quand** `downloadManager.IsComplete` est `true` :
    - Appeler [GetLargestVideoFilePath(downloadInfo)](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:309:4-365:5) pour obtenir `filePath`.
    - Construire un objet “persistable” à partir de `downloadInfo`.
    - Sérialiser en JSON (via `System.Text.Json`).
    - Écrire dans `Path.Combine(_downloadDirectory, $"{torrentId}.json")`.
- S’assurer de :
  - Gérer les exceptions d’IO (log + continuer).
  - Écraser le fichier si réécriture (re‑download du même film).

### 1.4. Reload au démarrage

- Dans le constructeur de [TorrentDownloadService](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:12:0-407:1) ou via une méthode privée `LoadPersistedTorrents()` appelée depuis le ctor :
  - Lister tous les fichiers `*.json` dans `_downloadDirectory`.
  - Pour chaque JSON :
    - Désérialiser en structure intermédiaire.
    - Vérifier que `File.Exists(filePath)` est encore vrai :
      - Si oui : reconstruire un [TorrentDownloadInfo](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:368:4-384:5) minimal :
        - Pas de `DownloadManager` (ou un stub), mais suffisamment pour :
          - Répondre à [GetProgressAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:128:4-157:5) (simulé : `Progress = 100`, `IsComplete = true`).
          - Répondre à [GetVideoFilePathAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:200:4-208:5) (retourner `filePath`).
      - Si non : ignorer ou marquer comme `status = "MissingFile"` (optionnel).
    - Ajouter cette entrée dans `_activeDownloads`.
- Décision de seeding :
  - **Itération 1** : ne pas relancer le seeding automatiquement.
  - Garder un TODO/extension possible pour appeler plus tard `TorrentSeedingService.RegisterTorrentForSeeding` avec un [TorrentDownloadManager](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.BitTorrent/TorrentDownloadManager.cs:13:0-1402:1) reconstitué.

### 1.5. Adaptations des endpoints

- [GetProgressAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:128:4-157:5) :
  - Si `DownloadManager` est null (cas rechargé depuis JSON) :
    - Retourner un DTO avec :
      - `Progress = 100`, `IsComplete = true`.
      - `DownloadSpeed = 0`.
      - `Status` = valeur rechargée ou `"Completed"`.
- [GetAllDownloadsAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:159:4-186:5) :
  - Même logique : inclure aussi les torrents chargés depuis JSON.

---

## 2. Page front minimaliste (liste + player)

### 2.1. Objectifs

- Démontrer le streaming et la persistance avec une UI très simple.
- Front unique page (SPA légère), par exemple :
  - Vite + React (ou même HTML/JS vanilla si tu préfères).
- Aucune logique complexe d’auth côté front (injection token manuelle OK pour l’instant).

### 2.2. Structure de la page

- **Section “Liste des torrents”** :
  - Requête `GET /api/torrents` au chargement.
  - Affichage d’un tableau / liste :
    - `movieTitle`
    - `progress` (barre ou pourcentage)
    - `status`
    - `isComplete` / `isReadyForStreaming`
  - Pour chaque ligne :
    - Bouton “Play” qui sélectionne ce torrent.

- **Section “Player vidéo”** :
  - Un simple `<video controls>` :
    - `src` = `http://localhost:5000/api/torrents/<selectedId>/stream`
    - `type` = `video/mp4`
  - Afficher sous le player :
    - Infos du torrent en cours (titre, état, progression).
  - (Optionnel) Bouton “Stop” qui appelle `/api/torrents/{id}/stop`.

### 2.3. Flux utilisateur typique

1. L’utilisateur ouvre la page.
2. La page charge la liste des torrents (`/api/torrents`).
3. L’utilisateur clique “Play” sur un film :
   - Le player `<video>` se met à streamer `/stream`.
4. Si le torrent est en cours de download (ex. 10–20 %), le player commence à lire dès que le flux est suffisant.
5. Si le backend a redémarré, mais la persistance est en place :
   - `/api/torrents` montre les films déjà téléchargés,
   - `Play` fonctionne directement sans devoir relancer un download.

### 2.4. Simplicité pour l’authentification

- Phase 1 :
  - Récupérer manuellement un token via Swagger / Postman.
  - Le coller dans une config front (variable d’environnement ou champ texte).
- Phase 2 (facultative) :
  - Ajouter une page “login” qui appelle ton endpoint d’auth et stocke le token dans `localStorage`.

---

## 3. ffprobe / remux : intégration ou tolérance

### 3.1. Option A — Intégration complète (recommandé pour respecter le sujet)

**Objectifs** :

- Déterminer si la vidéo est nativement lisible par le navigateur.
- Si oui :
  - Soit streamer directement,
  - Soit remuxer en MP4 pour simplifier le front.
- Si non :
  - Tenter une conversion plus lourde (selon les contraintes temps/CPU),
  - Ou marquer explicitement le film comme “Incompatible”.

**Étapes** :

1. **Installer ffmpeg/ffprobe dans Docker** :
   - Modifier `Dockerfile` pour installer `ffmpeg` (qui contient `ffprobe`).
   - Vérifier dans le conteneur que `ffprobe` est disponible dans le PATH.

2. **Configurer `FFMpegCore` / `VideoCodecDetector`** :
   - S’assurer que `FFMpegOptions` pointent vers `/usr/bin/ffprobe` (ou équivalent).
   - Gérer proprement les exceptions (timeout, erreurs d’analyse).

3. **Pipeline [StartConversionIfNeededAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:220:4-307:5)** :
   - Appeler `DetectCodecsAsync(videoFilePath)` :
     - Si `codec.VideoCodec == "h264"` et `codec.AudioCodec == "aac"` :
       - Lancer `RemuxToMp4Async(input, output)`.
       - Mettre `downloadInfo.ConvertedFilePath = output`.
     - Sinon :
       - Décider :
         - soit lancer un transcodage complet (H.264/AAC/MP4),
         - soit marquer `Status = "Incompatible"` avec un message explicite.

4. **Intégration avec `/progress` / `/stream`** :
   - [GetProgressAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:128:4-157:5) :
     - Si `ConvertedFilePath` est non null → `FilePath` = converti.
   - [GetVideoFilePathAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:200:4-208:5) :
     - Même logique : préférer `ConvertedFilePath` si présent.

### 3.2. Option B — Tolérer l’absence de ffprobe (pragmatique)

Si tu veux aller plus vite, tu peux :

1. Laisser `VideoCodecDetector` tel quel mais :
   - Si `ffprobe` manquant ou erreur :
     - Log,
     - `downloadInfo.Status = "Error"` (ou un statut spécifique, ex. `"CodecUnknown"`),
     - **ne pas bloquer** le streaming.

2. Côté front :
   - Afficher quand même le bouton Play pour les films avec `Progress = 100` même si `status = "Error"`.
   - Laisser le navigateur décider s’il sait lire ou non.

3. Plus tard, si un cas réel ne passe pas (ex. audio non supporté) :
   - Revenir à Option A pour les convertir proprement.

---

## Ordre d’implémentation recommandé

1. **Persistance JSON + reload au démarrage**  
   - T’éviter de relancer des téléchargements en boucle,
   - Permettre au front d’avoir une vraie “liste de films” persistante.

2. **Front minimal (liste + player)**  
   - Valider l’expérience de streaming du point de vue utilisateur.
   - Tester `Range`, buffering progressif, etc.

3. **ffprobe/remux** (Option A ou B)  
   - Option B (tolérer l’absence de ffprobe) est suffisante pour une démo fonctionnelle.
   - Option A est plus propre si tu vises une conformité stricte au sujet et une compatibilité multi‑navigateur.
