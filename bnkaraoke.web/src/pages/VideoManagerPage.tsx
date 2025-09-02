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
  YouTubeUrl: string | null;
  MusicBrainzId: string | null;
  LastFmPlaycount: number | null;
  Valence: number | null;
  NormalizationGain?: number | null;
  FadeStartTime?: number | null;
  IntroMuteDuration?: number | null;
  PreviewUrl?: string | null;
}

const VideoManagerPage: React.FC = () => {
  const navigate = useNavigate();
  const [songs, setSongs] = useState<SongVideo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [selectedSong, setSelectedSong] = useState<SongVideo | null>(null);
  const [showModal, setShowModal] = useState(false);
  const [fadeStartInput, setFadeStartInput] = useState("");
  const [introMuteInput, setIntroMuteInput] = useState("");
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [baseVolume, setBaseVolume] = useState(1);

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

  const fetchSongs = useCallback(async (token: string) => {
    try {
      const response = await fetch(`${API_ROUTES.SONGS_MANAGE}?page=1&pageSize=75`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (response.status === 401) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return;
      }
      if (!response.ok) throw new Error("Failed to fetch songs");
      const data = await response.json();
      const normalized = (data.songs || []).map((s: any) => ({
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
        YouTubeUrl: s.YouTubeUrl ?? s.youTubeUrl ?? s.youtubeUrl ?? null,
        Status: s.Status ?? s.status ?? "",
        MusicBrainzId: s.MusicBrainzId ?? s.musicBrainzId ?? null,
        LastFmPlaycount: s.LastFmPlaycount ?? s.lastFmPlaycount ?? null,
        Valence: s.Valence ?? s.valence ?? null,
        NormalizationGain: s.NormalizationGain ?? s.normalizationGain ?? null,
        FadeStartTime: s.FadeStartTime ?? s.fadeStartTime ?? null,
        IntroMuteDuration: s.IntroMuteDuration ?? s.introMuteDuration ?? null,
      } as SongVideo));
      setSongs(normalized);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  }, [navigate]);

  useEffect(() => {
    const token = validateToken();
    if (!token) return;
    fetchSongs(token);
  }, [validateToken, fetchSongs]);

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
    };

    video.volume = baseVolume;
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
        FadeStartTime: result.fadeStartTime ?? null,
        IntroMuteDuration: result.introMuteDuration ?? null,
        PreviewUrl: previewUrl,
      };
      setSongs((prev) => prev.map((s) => (s.Id === song.Id ? updated : s)));
      setSelectedSong(updated);
      setShowModal(true);
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
  };

  const handleFadeStartChange = (
    e: React.ChangeEvent<HTMLInputElement>
  ) => {
    const value = e.target.value;
    setFadeStartInput(value);
    const seconds = mmssToSeconds(value);
    updateSelected("FadeStartTime", seconds);
  };

  const handleIntroMuteChange = (
    e: React.ChangeEvent<HTMLInputElement>
  ) => {
    const value = e.target.value;
    setIntroMuteInput(value);
    updateSelected("IntroMuteDuration", parseFloat(value));
  };

  const handleApprove = async () => {
    if (!selectedSong) return;
    await handleSave(selectedSong);
    setSongs((prev) => prev.map((s) => (s.Id === selectedSong.Id ? selectedSong : s)));
    closeModal();
  };

  const pendingSongs = songs.filter(
    (s) =>
      s.Cached &&
      (s.NormalizationGain == null ||
        s.FadeStartTime == null ||
        s.IntroMuteDuration == null)
  );

  return (
    <div className="video-manager-container">
      <h2>Manage Videos</h2>
      {error && <p style={{ color: "red" }}>{error}</p>}
      <section>
        <h3>Songs Pending Analysis</h3>
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
      {showModal && selectedSong && (
        <div className="analysis-modal-overlay" onClick={closeModal}>
          <div className="analysis-modal" onClick={(e) => e.stopPropagation()}>
            <h3>
              {selectedSong.Title} - {selectedSong.Artist}
            </h3>
            {selectedSong.PreviewUrl && (
              <video ref={videoRef} src={selectedSong.PreviewUrl} controls />
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
