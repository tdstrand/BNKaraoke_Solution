import React, { useState, useEffect, useCallback } from "react";
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
  YouTubeUrl: string | null;
  Status: string;
  MusicBrainzId: string | null;
  LastFmPlaycount: number | null;
  Valence: number | null;
  NormalizationGain: number | null;
  FadeStartTime: number | null;
  IntroMuteDuration: number | null;
}

const VideoManagerPage: React.FC = () => {
  const navigate = useNavigate();
  const [songs, setSongs] = useState<SongVideo[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [selectedSong, setSelectedSong] = useState<SongVideo | null>(null);
  const [showModal, setShowModal] = useState(false);

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
      if (!response.ok) throw new Error("Failed to fetch songs");
      const data = await response.json();
      setSongs(data.songs || []);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  }, []);

  useEffect(() => {
    const token = validateToken();
    if (!token) return;
    fetchSongs(token);
  }, [validateToken, fetchSongs]);

  const openAnalysis = async (song: SongVideo) => {
    const token = validateToken();
    if (!token) return;
    try {
      const resp = await fetch(`${API_ROUTES.VIDEO_ANALYZE}/${song.Id}/analyze-video`, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!resp.ok) throw new Error(await resp.text());
      const result = await resp.json();
      const updated = { ...song, ...result };
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

  const handleApprove = async () => {
    if (!selectedSong) return;
    await handleSave(selectedSong);
    setSongs((prev) => prev.map((s) => (s.Id === selectedSong.Id ? selectedSong : s)));
    setShowModal(false);
    setSelectedSong(null);
  };

  const pendingSongs = songs.filter(
    (s) =>
      s.NormalizationGain === null ||
      s.FadeStartTime === null ||
      s.IntroMuteDuration === null
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
            {pendingSongs.map((s) => (
              <tr key={s.Id}>
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
        <div className="analysis-modal-overlay" onClick={() => setShowModal(false)}>
          <div className="analysis-modal" onClick={(e) => e.stopPropagation()}>
            <h3>
              {selectedSong.Title} - {selectedSong.Artist}
            </h3>
            {selectedSong.YouTubeUrl && (
              <iframe
                title="video-preview"
                src={selectedSong.YouTubeUrl.replace("watch?v=", "embed/")}
                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                allowFullScreen
              ></iframe>
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
                  type="number"
                  value={selectedSong.FadeStartTime ?? ""}
                  onChange={(e) => updateSelected("FadeStartTime", parseFloat(e.target.value))}
                />
              </label>
              <label>
                Intro Mute
                <input
                  type="number"
                  value={selectedSong.IntroMuteDuration ?? ""}
                  onChange={(e) => updateSelected("IntroMuteDuration", parseFloat(e.target.value))}
                />
              </label>
            </div>
            <div className="analysis-actions">
              <button onClick={handleApprove}>Approve</button>
              <button onClick={() => setShowModal(false)}>Cancel</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default VideoManagerPage;
