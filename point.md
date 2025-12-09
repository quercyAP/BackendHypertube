# Bilan de la journée – Pipeline MSE/HLS + sous-titres internes

## 1. Ce qui a été fait

### 1.1. Généralisation de l’entrée vidéo pour le HLS/MSE

- **Fichier**  
- [backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:0:0-0:0)
- **Fonction / méthode**  
- [GetFinalVideoPathForMseAsync(Guid torrentId, ...)](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:402:4-423:5)
- **Idée**  
- On n’exige plus `.mp4` uniquement : la méthode renvoie maintenant le meilleur fichier vidéo disponible (MP4 ou MKV web‑compatible), ce qui permet de packager HLS directement à partir d’un MKV H.264/AAC.

### 1.2. Détection des pistes de sous-titres internes

- **Fichiers**  
- [backend/src/Application/Hypertube.Application/Common/Services/IVideoCodecDetector.cs](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Application/Hypertube.Application/Common/Services/IVideoCodecDetector.cs:0:0-0:0)  
- [backend/src/Infrastructure/Hypertube.Infrastructure/Services/VideoCodecDetector.cs](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/VideoCodecDetector.cs:0:0-0:0)
- **Types / méthodes**  
- `class VideoCodecInfo`  
- `class SubtitleTrackInfo`  
- `Task<VideoCodecInfo?> DetectCodecsAsync(...)`
- **Idée**  
- [VideoCodecInfo](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Application/Hypertube.Application/Common/Services/IVideoCodecDetector.cs:4:0-13:1) contient maintenant une liste `SubtitleTracks`.  
- [VideoCodecDetector](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/VideoCodecDetector.cs:9:0-125:1) utilise `FFProbe.AnalyseAsync(...)` et renseigne les pistes `subtitle` (index global, codec).  
- Log de debug ajouté dans :
- [TorrentDownloadService.StartConversionIfNeededAsync(Guid torrentId)](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/TorrentDownloadService.cs:425:4-583:5)  
    pour afficher les pistes détectées (`[Subtitles] Detected internal subtitle track ...`).

### 1.3. Extraction automatique en WebVTT des sous-titres internes

- **Fichier**  
- [backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:0:0-0:0)
- **Méthodes**  
- `Task<string?> GetOrCreatePlaylistAsync(Guid torrentId, ...)`  
- `private async Task ExtractSubtitlesAsync(string inputPath, string torrentFolder, CancellationToken cancellationToken)`
- **Idée**  
- Après génération de la playlist HLS (`index.m3u8` + `seg_*.ts`), [GetOrCreatePlaylistAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:108:4-197:5) appelle [ExtractSubtitlesAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:152:4-222:5).  
- [ExtractSubtitlesAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:152:4-222:5) :
- rappelle [IVideoCodecDetector.DetectCodecsAsync](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/VideoCodecDetector.cs:23:4-96:5) pour récupérer `SubtitleTracks`,
- filtre les codecs texte (`subrip`, `ass`, `mov_text`, `webvtt`),
- extrait chaque piste vers `sub_{Index}.vtt` dans le dossier HLS du torrent :
    - `wwwroot/hls/{torrentIdN}/sub_*.vtt`,
- extraction best‑effort (les erreurs de sous‑titres ne cassent pas le packaging HLS).

### 1.4. Exposition des sous-titres via API dédiée MSE

- **Fichier**  
- [backend/src/WebAPI/Hypertube.WebAPI/Controllers/MseController.cs](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/Controllers/MseController.cs:0:0-0:0)
- **Méthodes**  
- `Task<IActionResult> GetHlsPlaylist(Guid torrentId, ...)` (déjà existante)  
- `IActionResult GetSubtitles(Guid torrentId)`
- **Idée**  
- [MseController](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/Controllers/MseController.cs:8:0-89:1) injecte désormais aussi `IWebHostEnvironment` (en plus de `IHlsPackagingService`, `ILogger`).  
- Nouveau endpoint :
- `GET /api/mse/subtitles/{torrentId}`  
- Parcourt `wwwroot/hls/{torrentIdN}/`  
- Liste tous les fichiers `sub_*.vtt` et renvoie un tableau JSON :
    - `[{ fileName: \"sub_3.vtt\", url: \"/hls/{idN}/sub_3.vtt\" }, ...]`.

### 1.5. Adaptation du player HLS/MSE de test pour consommer ces sous-titres

- **Fichier**  
- [backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html:0:0-0:0)
- **Parties modifiées**  
- Section HTML :
- Ajout d’un `<select id=\"subtitle-select\">` (options dynamiques) sous la vidéo.
- Logique JS :
- Variables :
    - `const subtitleSelect = document.getElementById(\"subtitle-select\");`
    - `let hlsInstance = null;`
    - `let currentTorrentId = null;`
- Dans [playTorrent(download)](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html:452:6-483:7) :
    - Appel de `GET /api/mse/hls/{torrentId}` pour obtenir `playlistUrl`.
    - Mémorisation du torrent courant : `currentTorrentId = id;`
    - Appel de `await loadSubtitlesForTorrent(id);`
    - Puis `await attachHls(playlistUrl);`
- Nouveaux helpers :
    - `async function loadSubtitlesForTorrent(torrentId)`
    - Appelle `/api/mse/subtitles/{torrentId}`.
    - Reconstruit les options du `<select>` (`value = s.url`, `label = s.fileName`).
    - `function clearManagedSubtitleTracks()`
    - Supprime les `<track>` ajoutés par le script (`data-managed="true"`).
    - `function applySelectedSubtitles()`
    - Récupère `subtitleSelect.value` (URL complète du `.vtt`).
    - Ajoute dynamiquement un `<track kind=\"subtitles\" src=\"...\">` sur la balise `<video>`.
    - Force `textTrack.mode = 'showing'` pour les pistes `subtitles`.
- Intégration avec `hls.js` :
    - Dans [attachHls(src)](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html:485:6-519:7) :
    - Sur `Hls.Events.MANIFEST_PARSED`, ré‑applique la sélection courante :
        - [applySelectedSubtitles();](cci:1://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html:528:6-563:7)
- Gestion des événements :
    - `subtitleSelect.addEventListener(\"change\", () => applySelectedSubtitles());`

### 1.6. Ajustements DI / lifetimes

- **Fichier**  
- [backend/src/WebAPI/Hypertube.WebAPI/Program.cs](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/Program.cs:0:0-0:0)
- **Changements**  
- Enregistrement des services vidéo :
- `IVideoConversionService` → `AddScoped`
- [IVideoCodecDetector](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Application/Hypertube.Application/Common/Services/IVideoCodecDetector.cs:28:0-33:1) → `AddScoped`
- `IVideoRemuxService` → `AddScoped`
- `IHlsPackagingService` → **`AddScoped`** (au lieu de `AddSingleton`)  
    car [HlsPackagingService](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/HlsPackagingService.cs:9:0-223:1) dépend de services scoped ([IVideoCodecDetector](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Application/Hypertube.Application/Common/Services/IVideoCodecDetector.cs:28:0-33:1)).

---

## 2. Ce qu’il reste à faire (pistes futures)

### 2.1. Généraliser et enrichir les métadonnées de sous-titres

- Ajouter, côté détection ([VideoCodecDetector](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Infrastructure/Hypertube.Infrastructure/Services/VideoCodecDetector.cs:9:0-125:1)), des infos supplémentaires par [SubtitleTrackInfo](cci:2://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/Application/Hypertube.Application/Common/Services/IVideoCodecDetector.cs:15:0-26:1) :
- langue (`Language`),
- titre / rôle (`Title`, `IsForced` si dispo dans les métadonnées).
- Propager ces infos jusqu’au JSON renvoyé par `/api/mse/subtitles/{torrentId}` (au lieu de n’exposer que `fileName` / `url`).

### 2.2. Gérer plusieurs pistes de sous-titres internes

- Gérer plusieurs `sub_*.vtt` par torrent :
- UI côté player pour distinguer les différentes pistes (ex : `FR`, `EN`, `EN (Signs)`, etc.).
- Politique de nommage ou métadonnées (ex : `sub_{Index}_{lang}.vtt`).

### 2.3. Support des sous-titres externes

- Pipeline (à concevoir) :
- Upload ou récupération de fichiers `.srt` externes.
- Conversion `.srt` → `.vtt` (via FFmpeg ou utilitaire dédié).
- Stockage dans un dossier dédié par torrent (ou réutilisation du dossier HLS).
- Ajout de ces pistes externes dans la réponse `/api/mse/subtitles/{torrentId}` au même titre que les internes.

### 2.4. Interaction avec le futur front “réel”

- Fournir au dev front (F#/React) :
- la liste des endpoints :
- `/api/torrents`
- `/api/mse/hls/{torrentId}`
- `/api/mse/subtitles/{torrentId}`
- la logique de base illustrée dans [hls-player.html](cci:7://file:///home/jgiampor/Desktop/BackendHypertube/backend/src/WebAPI/Hypertube.WebAPI/wwwroot/hls-player.html:0:0-0:0) :
- usage de `hls.js` pour `playlistUrl`,
- usage de `<track>` + `textTracks` pour les sous-titres.
- Le vrai front pourra ensuite :
- refaire une UI propre autour de cette API,
- intégrer la sélection des sous-titres de manière plus ergonomique (menu, icône CC, etc.).

---