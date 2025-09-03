import React, { useState, useEffect, useCallback, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "./VideoManagerPage.css";

interface SongVideo {
  Id: number;
  Title: string;
  Artist: string;
  Genre: string | null;
  Decade: string | null;
  Bpm: number | null;
  Danceability: string | null;
  Energy: string | null;
  Mood: string | null;
  Popularity: number | null;
  SpotifyId: string | null;
  Status: string;
  Cached: boolean;
  Analyzed: boolean;
  YouTubeUrl: string | null;
  MusicBrainzId: string | null;
  LastFmPlaycount: number | null;
  Valence: number | null;
  NormalizationGain?: number | null;
  FadeStartTime?: number | null;
  IntroMuteDuration?: number | null;
  PreviewUrl?: string | null;
}

interface SongApi extends Partial<SongVideo> {
  id?: number;
  title?: string;
  artist?: string;
  genre?: string | null;
  decade?: string | null;
  bpm?: number | null;
  danceability?: string | null;
  energy?: string | null;
  mood?: string | null;
  popularity?: number | null;
  spotifyId?: string | null;
  cached?: boolean;
  analyzed?: boolean;
  youTubeUrl?: string | null;
  youtubeUrl?: string | null;
  status?: string;
  musicBrainzId?: string | null;
  lastFmPlaycount?: number | null;
  valence?: number | null;
  normalizationGain?: number | null;
  fadeStartTime?: number | null;
  introMuteDuration?: number | null;
}

interface SearchResult {
  Id: number;
  Title: string;
  Artist: string;
}

const VideoManagerPage: React.FC = () => {
  const navigate = useNavigate();
  const [songs, setSongs] = useState<SongVideo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [selectedSong, setSelectedSong] = useState<SongVideo | null>(null);
  const [showModal, setShowModal] = useState(false);
  const [fadeStartInput, setFadeStartInput] = useState("");
  const [introMuteInput, setIntroMuteInput] = useState("");
  const [analysisInfo, setAnalysisInfo] = useState<
    | {
        normalizationGain: number | null;
        inputLoudness?: number | null;
        duration?: number | null;
        inputTruePeak?: number | null;
        inputLra?: number | null;
        inputThreshold?: number | null;
        summary?: string | null;
      }
    | null
  >(null);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [baseVolume, setBaseVolume] = useState(1);
  const [analyzingSong, setAnalyzingSong] = useState<SongVideo | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [searching, setSearching] = useState(false);
  const [pendingCount, setPendingCount] = useState(0);

  const secondsToMmss = (secs: number) => {
    const m = Math.floor(secs / 60);
    const s = Math.floor(secs % 60)
      .toString()
      .padStart(2, "0");
    return `${m}:${s}`;
  };

  const mmssToSeconds = (value: string): number => {
    const parts = value.split(":");
    if (parts.length !== 2) return NaN;
    const m = parseInt(parts[0], 10);
    const s = parseFloat(parts[1]);
    if (isNaN(m) || isNaN(s)) return NaN;
    return m * 60 + s;
  };

  const closeModal = useCallback(() => {
    setShowModal(false);
    if (selectedSong?.PreviewUrl) {
      URL.revokeObjectURL(selectedSong.PreviewUrl);
    }
    setSelectedSong(null);
    setAnalysisInfo(null);
  }, [selectedSong]);

  const validateToken = useCallback(() => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      navigate("/login");
      return null;
    }
    return token;
  }, [navigate]);

  const fetchSongs = useCallback(
    async (token: string) => {
      try {
        let page = 1;
        const pageSize = 150;
        let allSongs: SongVideo[] = [];
        let totalPages = 1;
        do {
          const response = await fetch(
            `${API_ROUTES.SONGS_MANAGE}?page=${page}&pageSize=${pageSize}`,
            { headers: { Authorization: `Bearer ${token}` } }
          );
          if (response.status === 401) {
            localStorage.removeItem("token");
            localStorage.removeItem("userName");
            navigate("/login");
            return;
          }
          if (!response.ok) throw new Error("Failed to fetch songs");
          const data: { songs: SongApi[]; totalPages?: number } =
            await response.json();
          const normalized = (data.songs || []).map((s) => ({
            Id: s.Id ?? s.id,
            Title: s.Title ?? s.title ?? "",
            Artist: s.Artist ?? s.artist ?? "",
            Genre: s.Genre ?? s.genre ?? null,
            Decade: s.Decade ?? s.decade ?? null,
            Bpm: s.Bpm ?? s.bpm ?? null,
            Danceability: s.Danceability ?? s.danceability ?? null,
            Energy: s.Energy ?? s.energy ?? null,
            Mood: s.Mood ?? s.mood ?? null,
            Popularity: s.Popularity ?? s.popularity ?? null,
            SpotifyId: s.SpotifyId ?? s.spotifyId ?? null,
            Cached: s.Cached ?? s.cached ?? false,
            Analyzed: s.Analyzed ?? s.analyzed ?? false,
            YouTubeUrl: s.YouTubeUrl ?? s.youTubeUrl ?? s.youtubeUrl ?? null,
            Status: s.Status ?? s.status ?? "",
            MusicBrainzId: s.MusicBrainzId ?? s.musicBrainzId ?? null,
            LastFmPlaycount: s.LastFmPlaycount ?? s.lastFmPlaycount ?? null,
            Valence: s.Valence ?? s.valence ?? null,
            NormalizationGain: s.NormalizationGain ?? s.normalizationGain ?? null,
            FadeStartTime:
              (s.FadeStartTime ?? s.fadeStartTime ?? 0) > 0
                ? (s.FadeStartTime ?? s.fadeStartTime ?? 0)
                : null,
            IntroMuteDuration:
              (s.IntroMuteDuration ?? s.introMuteDuration ?? 0) > 0
                ? (s.IntroMuteDuration ?? s.introMuteDuration ?? 0)
                : null,
          } as SongVideo));
          allSongs = allSongs.concat(normalized);
          totalPages = data.totalPages ?? 1;
          page++;
        } while (page <= totalPages);
        setSongs(allSongs);
      } catch (err) {
        const message = err instanceof Error ? err.message : "Unknown error";
        setError(message);
      }
    },
    [navigate]
  );

  const fetchPendingCount = useCallback(
    async (token: string) => {
      try {
        let page = 1;
        const pageSize = 150;
        let total = 0;
        let totalPages = 1;
        do {
          const resp = await fetch(
            `${API_ROUTES.SONGS_MANAGE}?page=${page}&pageSize=${pageSize}`,
            { headers: { Authorization: `Bearer ${token}` } }
          );
          if (!resp.ok) throw new Error("Failed to fetch pending count");
          const data: { songs: SongApi[]; totalPages?: number } = await resp.json();
          for (const s of data.songs || []) {
            const cached = s.Cached ?? s.cached ?? false;
            const analyzed = s.Analyzed ?? s.analyzed ?? false;
            if (cached && !analyzed) total++;
          }
          totalPages = data.totalPages ?? 1;
          page++;
        } while (page <= totalPages);
        setPendingCount(total);
      } catch (err) {
        const message = err instanceof Error ? err.message : "Unknown error";
        setError(message);
      }
    },
    [setPendingCount]
  );

  useEffect(() => {
    const loadSongs = async () => {
      const token = validateToken();
      if (!token) return;
      try {
        await fetchSongs(token);
        await fetchPendingCount(token);
      } catch (err) {
        console.error("[VIDEO_MANAGER] Failed to fetch songs", err);
      }
    };
    loadSongs();
  }, [validateToken, fetchSongs, fetchPendingCount]);

  useEffect(() => {
    if (selectedSong) {
      setFadeStartInput(
        selectedSong.FadeStartTime != null
          ? secondsToMmss(selectedSong.FadeStartTime)
          : ""
      );
      setIntroMuteInput(
        selectedSong.IntroMuteDuration != null
          ? selectedSong.IntroMuteDuration.toFixed(1)
          : ""
      );
      const gain = selectedSong.NormalizationGain ?? 0;
      const vol = Math.pow(10, gain / 20);
      setBaseVolume(Math.min(Math.max(vol, 0), 1));
    }
  }, [selectedSong]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !showModal) return;

    const handleTimeUpdate = () => {
      if (!selectedSong) return;
      const muteDur = selectedSong.IntroMuteDuration ?? 0;
      const fadeStart = selectedSong.FadeStartTime ?? Infinity;
      const fadeDur = 7;
      const stopTime = fadeStart + 8;
      let volume = baseVolume;

      if (video.currentTime < muteDur) {
        volume = 0;
      } else if (
        video.currentTime >= fadeStart &&
        video.currentTime <= fadeStart + fadeDur
      ) {
        const progress = (video.currentTime - fadeStart) / fadeDur;
        volume = baseVolume * (1 - progress);
      } else if (video.currentTime > fadeStart + fadeDur) {
        volume = 0;
      }
      video.volume = Math.min(Math.max(volume, 0), 1);
      if (video.currentTime >= stopTime) {
        video.pause();
      }
    };

    video.volume = baseVolume;
    video.muted = false;
    video.currentTime = 0;
    handleTimeUpdate();
    video
      .play()
      .catch((err) => {
        // Log the error rather than using an empty handler to satisfy lint rules
        console.error("Video playback failed", err);
      });
    video.addEventListener("timeupdate", handleTimeUpdate);
    video.addEventListener("loadedmetadata", handleTimeUpdate);

    return () => {
      video.removeEventListener("timeupdate", handleTimeUpdate);
      video.removeEventListener("loadedmetadata", handleTimeUpdate);
    };
  }, [selectedSong, baseVolume, showModal]);

  const openAnalysis = async (song: SongVideo) => {
    const token = validateToken();
    if (!token) return;
    setAnalyzingSong(song);
    try {
      const resp = await fetch(`${API_ROUTES.VIDEO_ANALYZE}/${song.Id}/analyze-video`, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (resp.status === 401) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return;
      }
      if (!resp.ok) throw new Error(await resp.text());
      const result = await resp.json();
      setAnalysisInfo({
        normalizationGain: result.normalizationGain ?? null,
        inputLoudness: result.inputLoudness ?? null,
        duration: result.duration ?? null,
        inputTruePeak: result.inputTruePeak ?? null,
        inputLra: result.inputLoudnessRange ?? null,
        inputThreshold: result.inputThreshold ?? null,
        summary: result.summary ?? null,
      });
      let previewUrl: string | null = null;
      try {
        const videoResp = await fetch(`${API_ROUTES.CACHE_VIDEO}/${song.Id}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (videoResp.ok) {
          const blob = await videoResp.blob();
          previewUrl = URL.createObjectURL(blob);
        }
      } catch {
        previewUrl = null;
      }

      if (!previewUrl) {
        setError("Cached video not found");
        return;
      }

      const updated = {
        ...song,
        NormalizationGain: result.normalizationGain ?? null,
        PreviewUrl: previewUrl,
      };
      setSelectedSong(updated);
      setShowModal(true);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    } finally {
      setAnalyzingSong(null);
    }
  };

  const openEdit = useCallback(
    async (song: SongVideo) => {
      const token = validateToken();
      if (!token) return;
      try {
        let previewUrl: string | null = null;
        try {
          const videoResp = await fetch(`${API_ROUTES.CACHE_VIDEO}/${song.Id}`, {
            headers: { Authorization: `Bearer ${token}` },
          });
          if (videoResp.ok) {
            const blob = await videoResp.blob();
            previewUrl = URL.createObjectURL(blob);
          }
        } catch {
          previewUrl = null;
        }
        if (!previewUrl) {
          setError("Cached video not found");
          return;
        }
        const updated = { ...song, PreviewUrl: previewUrl };
        setSelectedSong(updated);
        setAnalysisInfo(null);
        setShowModal(true);
      } catch (err) {
        const message = err instanceof Error ? err.message : "Unknown error";
        setError(message);
      }
    },
    [validateToken]
  );

  const handleSearch = async () => {
    const token = validateToken();
    if (!token) return;
    setSearching(true);
    try {
      const resp = await fetch(
        `${API_ROUTES.SONGS_SEARCH}?query=${encodeURIComponent(searchQuery)}`,
        { headers: { Authorization: `Bearer ${token}` } }
      );
      if (resp.status === 401) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return;
      }
      if (!resp.ok) throw new Error("Search failed");
        const data = await resp.json();
        const results = (data.songs || []).map((s: SongApi) => ({
        Id: s.Id ?? s.id,
        Title: s.Title ?? s.title ?? "",
        Artist: s.Artist ?? s.artist ?? "",
      }));
      setSearchResults(results);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    } finally {
      setSearching(false);
    }
  };

  const handleSelectSong = async (res: SearchResult) => {
    const token = validateToken();
    if (!token) return;
    try {
      const resp = await fetch(`${API_ROUTES.SONG_BY_ID}/${res.Id}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (resp.status === 401) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return;
      }
      if (!resp.ok) throw new Error("Failed to fetch song");
      const data: SongApi = await resp.json();
      const song: SongVideo = {
        Id: data.Id ?? data.id ?? 0,
        Title: data.Title ?? data.title ?? "",
        Artist: data.Artist ?? data.artist ?? "",
        Genre: data.Genre ?? data.genre ?? null,
        Decade: data.Decade ?? data.decade ?? null,
        Bpm: data.Bpm ?? data.bpm ?? null,
        Danceability: data.Danceability ?? data.danceability ?? null,
        Energy: data.Energy ?? data.energy ?? null,
        Mood: data.Mood ?? data.mood ?? null,
        Popularity: data.Popularity ?? data.popularity ?? null,
        SpotifyId: data.SpotifyId ?? data.spotifyId ?? null,
        Status: data.Status ?? data.status ?? "",
        Cached: data.Cached ?? data.cached ?? false,
        Analyzed: data.Analyzed ?? data.analyzed ?? false,
        YouTubeUrl: data.YouTubeUrl ?? data.youTubeUrl ?? data.youtubeUrl ?? null,
        MusicBrainzId: data.MusicBrainzId ?? data.musicBrainzId ?? null,
        LastFmPlaycount: data.LastFmPlaycount ?? data.lastFmPlaycount ?? null,
        Valence: data.Valence ?? data.valence ?? null,
        NormalizationGain: data.NormalizationGain ?? data.normalizationGain ?? null,
        FadeStartTime: data.FadeStartTime ?? data.fadeStartTime ?? null,
        IntroMuteDuration: data.IntroMuteDuration ?? data.introMuteDuration ?? null,
      };
      await openEdit(song);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  };

  const handleSave = async (song: SongVideo) => {
    const token = validateToken();
    if (!token) return;
    try {
      const resp = await fetch(`${API_ROUTES.SONG_UPDATE}/${song.Id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(song),
      });
      if (!resp.ok) throw new Error("Update failed");
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  };

  const updateSelected = (field: keyof SongVideo, value: number) => {
    setSelectedSong((prev) =>
      prev ? { ...prev, [field]: isNaN(value) ? null : value } : prev
    );
    if (field === "NormalizationGain" && !isNaN(value)) {
      const vol = Math.pow(10, value / 20);
      setBaseVolume(Math.min(Math.max(vol, 0), 1));
    }
  };

  const handleFadeStartChange = (
    e: React.ChangeEvent<HTMLInputElement>
  ) => {
    const value = e.target.value;
    setFadeStartInput(value);
    if (value === "") {
      updateSelected("FadeStartTime", NaN);
    } else if (/^\d+:\d{2}$/.test(value)) {
      const seconds = mmssToSeconds(value);
      updateSelected("FadeStartTime", seconds > 0 ? seconds : NaN);
    }
  };

  const handleIntroMuteChange = (
    e: React.ChangeEvent<HTMLInputElement>
  ) => {
    const value = e.target.value;
    setIntroMuteInput(value);
    if (value === "") {
      updateSelected("IntroMuteDuration", NaN);
    } else if (/^\d+(\.\d+)?$/.test(value)) {
      const num = parseFloat(value);
      updateSelected("IntroMuteDuration", num > 0 ? num : NaN);
    }
  };

  const handleApprove = async () => {
    if (!selectedSong) return;
    const approved = { ...selectedSong, Analyzed: true };
    await handleSave(approved);
    setSongs((prev) => prev.map((s) => (s.Id === approved.Id ? approved : s)));
    setPendingCount((prev) => Math.max(prev - 1, 0));
    closeModal();
  };

  const pendingSongs = songs.filter((s) => s.Cached && !s.Analyzed);

  const handleRefresh = useCallback(async () => {
    const token = validateToken();
    if (!token) return;
    try {
      await fetchSongs(token);
      await fetchPendingCount(token);
    } catch (err) {
      console.error("[VIDEO_MANAGER] Refresh failed", err);
    }
  }, [validateToken, fetchSongs, fetchPendingCount]);

  return (
    <div className="video-manager-container">
      <div className="video-manager-header">
        <h2>Manage Videos</h2>
        <button className="back-button" onClick={() => navigate("/dashboard")}>Back to Dashboard</button>
      </div>
      {error && <p style={{ color: "red" }}>{error}</p>}
      <section className="search-section">
        <h3>Search for Song</h3>
        <div className="search-controls">
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Enter title or artist"
          />
          <button onClick={handleSearch} disabled={searching}>
            Search
          </button>
        </div>
        <table>
          <thead>
            <tr>
              <th>Id</th>
              <th>Title</th>
              <th>Artist</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {searchResults.map((r) => (
              <tr key={r.Id}>
                <td>{r.Id}</td>
                <td>{r.Title}</td>
                <td>{r.Artist}</td>
                <td>
                  <button onClick={() => handleSelectSong(r)}>Edit</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
      <section className="pending-section">
        <div className="pending-header">
          <h3>
            Songs Pending Analysis ({pendingCount} songs pending)
          </h3>
          <button
            className="refresh-button"
            onClick={handleRefresh}
            aria-label="Refresh pending songs"
          >
            ↻
          </button>
        </div>
        <table>
          <thead>
            <tr>
              <th>Index</th>
              <th>Title</th>
              <th>Artist</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {pendingSongs.map((s, idx) => (
              <tr key={s.Id ?? idx}>
                <td>{s.Id}</td>
                <td>{s.Title}</td>
                <td>{s.Artist}</td>
                <td>
                  <button onClick={() => openAnalysis(s)}>Analyze</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
      {analyzingSong && (
        <div className="analysis-modal-overlay">
          <div className="analysis-modal">
            <p>
              Analyzing Video: {analyzingSong.Title} - {analyzingSong.Artist}
            </p>
          </div>
        </div>
      )}
      {showModal && selectedSong && (
        <div className="analysis-modal-overlay" onClick={closeModal}>
          <div className="analysis-modal" onClick={(e) => e.stopPropagation()}>
            <h3>
              {selectedSong.Title} - {selectedSong.Artist}
            </h3>
            {selectedSong.PreviewUrl && (
              <video ref={videoRef} src={selectedSong.PreviewUrl} controls />
            )}
            {analysisInfo && (
              <div className="analysis-details">
                {analysisInfo.inputLoudness != null && (
                  <p>Detected loudness: {analysisInfo.inputLoudness.toFixed(2)} LUFS</p>
                )}
                {analysisInfo.inputTruePeak != null && (
                  <p>Input true peak: {analysisInfo.inputTruePeak.toFixed(2)} dBTP</p>
                )}
                {analysisInfo.inputLra != null && (
                  <p>Input LRA: {analysisInfo.inputLra.toFixed(2)} LU</p>
                )}
                {analysisInfo.inputThreshold != null && (
                  <p>Input threshold: {analysisInfo.inputThreshold.toFixed(2)} dBFS</p>
                )}
                {analysisInfo.duration != null && (
                  <p>Video duration: {secondsToMmss(analysisInfo.duration)}</p>
                )}
                <p>
                  Recommended gain: {" "}
                  {analysisInfo.normalizationGain != null
                    ? analysisInfo.normalizationGain.toFixed(2)
                    : "n/a"}
                  {" "}dB
                </p>
                {analysisInfo.summary && (
                  <pre className="loudnorm-summary">{analysisInfo.summary}</pre>
                )}
              </div>
            )}
            <div className="analysis-fields">
              <label>
                Gain
                <input
                  type="number"
                  value={selectedSong.NormalizationGain ?? ""}
                  onChange={(e) => updateSelected("NormalizationGain", parseFloat(e.target.value))}
                />
              </label>
              <label>
                Fade Start
                <input
                  type="text"
                  placeholder="mm:ss"
                  value={fadeStartInput}
                  onChange={handleFadeStartChange}
                />
              </label>
              <label>
                Intro Mute
                <input
                  type="number"
                  step="0.1"
                  value={introMuteInput}
                  onChange={handleIntroMuteChange}
                />
              </label>
            </div>
            <div className="analysis-actions">
              <button onClick={handleApprove}>Approve</button>
              <button onClick={closeModal}>Cancel</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default VideoManagerPage;
