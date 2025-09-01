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

  const handleAnalyze = async (id: number) => {
    const token = validateToken();
    if (!token) return;
    try {
      const resp = await fetch(`${API_ROUTES.VIDEO_ANALYZE}/${id}/analyze-video`, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!resp.ok) throw new Error(await resp.text());
      const result = await resp.json();
      setSongs((prev) => prev.map((s) => (s.Id === id ? { ...s, ...result } : s)));
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

  const updateField = (id: number, field: keyof SongVideo, value: number) => {
    setSongs((prev) =>
      prev.map((s) => (s.Id === id ? { ...s, [field]: isNaN(value) ? null : value } : s))
    );
  };

  return (
    <div className="video-manager-container">
      <h2>Manage Videos</h2>
      {error && <p style={{ color: "red" }}>{error}</p>}
      <table>
        <thead>
          <tr>
            <th>Title</th>
            <th>Artist</th>
            <th>Gain</th>
            <th>Fade Start</th>
            <th>Intro Mute</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {songs.map((s) => (
            <tr key={s.Id}>
              <td>{s.Title}</td>
              <td>{s.Artist}</td>
              <td>
                <input
                  type="number"
                  value={s.NormalizationGain ?? ""}
                  onChange={(e) => updateField(s.Id, "NormalizationGain", parseFloat(e.target.value))}
                />
              </td>
              <td>
                <input
                  type="number"
                  value={s.FadeStartTime ?? ""}
                  onChange={(e) => updateField(s.Id, "FadeStartTime", parseFloat(e.target.value))}
                />
              </td>
              <td>
                <input
                  type="number"
                  value={s.IntroMuteDuration ?? ""}
                  onChange={(e) => updateField(s.Id, "IntroMuteDuration", parseFloat(e.target.value))}
                />
              </td>
              <td>
                <button onClick={() => handleAnalyze(s.Id)}>Analyze</button>
                <button onClick={() => handleSave(s)}>Save</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default VideoManagerPage;
