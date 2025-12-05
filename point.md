Bonne question, c’est exactement ce qu’il faudra expliquer en soutenance.

## 1. Ce qu’on avait avant

- **Player classique**  
  - `/api/torrents/{id}/stream` → HTTP Range direct sur un seul MP4.  
  - OK pour une vidéo déjà prête, mais :
    - pas d’adaptation réseau,
    - pas de vraie logique de buffering fin,
    - pas de manifest (on ne voit pas les segments côté réseau).

- **Tentatives MSE “maison”** (plans 4/5)  
  - On générait nous‑mêmes des segments MP4 via `ffmpeg -f segment`.  
  - Problème : fMP4/boxes non conformes → MSE refusait de jouer (erreurs silencieuses ou fatales).  
  - Tout le packaging MSE était “fait main”, très fragile.

## 2. Nouvelle approche : MP4 maison → HLS/MSE “industriel”

### Avantages techniques

- **On garde la pédagogie du sujet**  
  - Toute la partie **torrent → fichier → détecter codecs → remux/transcodage** reste 100% maison (C# + FFmpeg-core).
  - On n’utilise **aucune lib “torrent → stream”** (webtorrent, peerflix, etc.).

- **On délègue la partie la plus complexe**  
  - Le packaging HLS (manifest `.m3u8`, segments `.ts`) est fait par FFmpeg, qui respecte les specs HLS.  
  - La gestion MSE (buffer, erreurs réseau, reconnections) est faite par `hls.js` dans le navigateur.

- **Plus robuste côté player**  
  - HLS est un **standard** : manifest + segments clairement définis.  
  - `hls.js` gère :
    - parsing du manifest,
    - récupération des segments,
    - injection dans MSE,
    - gestion d’erreurs et de retry.

- **Plus démonstratif**  
  - Tu peux montrer dans l’onglet Network du navigateur :
    - la requête [index.m3u8](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls/db0f2dd72afc4174a4544b2650c1babf/home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls/db0f2dd72afc4174a4544b2650c1babf/index.m3u8:0:0-0:0),
    - la liste des segments `seg_00000.ts`, `seg_00001.ts`, etc.  
  - Ça illustre très bien “streaming segmenté” par rapport au gros MP4 unique.

## 3. Quelles libs, où, comment ?

### Côté backend

- **Lib principale** : `FFMpegCore` (wrapper C# autour de `ffmpeg` CLI).  
- **Utilisation HLS** :
  - Classe : [HlsPackagingService](cci:2://file:///home/Administrateur/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:9:0-137:1)  
    [backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:0:0-0:0)
  - Interface exposée : [IHlsPackagingService](cci:2://file:///home/Administrateur/BackendHypertube/backend/src/Application/Hypertube.Application/Common/Services/IHlsPackagingService.cs:6:0-13:1)  
    [backend/src/Application/Hypertube.Application/Common/Services/IHlsPackagingService.cs](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/Application/Hypertube.Application/Common/Services/IHlsPackagingService.cs:0:0-0:0)
  - Enregistrement DI :  
    `builder.Services.AddSingleton<IHlsPackagingService, HlsPackagingService>();`  
    dans [Program.cs](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/Program.cs:0:0-0:0).
  - Endpoint API :
    - [MseController.GetHlsPlaylist(Guid torrentId)](cci:1://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/Controllers/MseController.cs:20:4-46:5)  
      `GET /api/mse/hls/{torrentId}`  
      [backend/src/WebAPI/Hypertube.WebAPI/Controllers/MseController.cs](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/Controllers/MseController.cs:0:0-0:0)
    - Il appelle [GetOrCreatePlaylistAsync](cci:1://file:///home/Administrateur/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:34:4-120:5) qui :
      - récupère le MP4 final via [ITorrentDownloadService.GetFinalVideoPathForMseAsync](cci:1://file:///home/Administrateur/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:402:4-430:5),
      - lance `ffmpeg` pour générer HLS sous `wwwroot/hls/{torrentIdN}/index.m3u8`.

- **Static files** :
  - Mapping explicite [/hls](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls:0:0-0:0) → [wwwroot/hls](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls:0:0-0:0) dans [Program.cs](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/Program.cs:0:0-0:0) avec `UseStaticFiles`.

### Côté frontend

- **Lib utilisée** : `hls.js` (côté navigateur, pour HLS + MSE).  
- **Où** :  
  - Fichier : [backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html](cci:7://file:///home/Administrateur/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html:0:0-0:0)  
  - Chargement :  
    ```html
    <script src="https://cdn.jsdelivr.net/npm/hls.js@1.5.5/dist/hls.min.js" defer></script>
    ```
- **Comment** :
  - La page :
    - liste les torrents via `/api/torrents` (JWT dans `Authorization: Bearer …`),
    - appelle `/api/mse/hls/{torrentId}` pour obtenir `playlistUrl`,
    - crée/attache un `Hls()` si `Hls.isSupported()` :
      ```js
      const hls = new Hls({ enableWorker: true });
      hls.loadSource(playlistUrl);
      hls.attachMedia(videoElement);
      hls.on(Hls.Events.MANIFEST_PARSED, () => videoElement.play());
      ```
    - fallback natif HLS (`video.canPlayType('application/vnd.apple.mpegurl')`) pour Safari.

## 4. Message à faire passer en soutenance

- **Avant** :  
  - soit MP4 direct (simple, mais pas vraiment MSE / streaming segmenté),  
  - soit MSE bricolé avec des segments non conformes.
- **Maintenant** :  
  - pipeline torrent+conversion **maison**,  
  - packaging HLS + lecture MSE assurés par des outils pros (`ffmpeg` + `hls.js`),  
  - totalement dans les règles du sujet (on ne fait jamais “torrent → stream” via une lib).  

Si tu veux, je peux t’écrire un petit paragraphe “Avantages de l’approche HLS/MSE” à copier-coller dans ton rapport ou [plan6.md](cci:7://file:///home/Administrateur/BackendHypertube/plan6.md:0:0-0:0).