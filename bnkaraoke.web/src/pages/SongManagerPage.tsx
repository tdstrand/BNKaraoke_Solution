// src/pages/SongManagerPage.tsx
import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "./SongManagerPage.css";

interface SongUpdate {
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
}

const SongManagerPage: React.FC = () => {
  const navigate = useNavigate();
  const [manageableSongs, setManageableSongs] = useState<SongUpdate[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [editSong, setEditSong] = useState<SongUpdate | null>(null);
  const [filterQuery, setFilterQuery] = useState("");
  const [filterArtist, setFilterArtist] = useState("");
  const [filterStatus, setFilterStatus] = useState("");

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split(".").length !== 3) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split(".")[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      return token;
    } catch (err) {
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  const fetchManageableSongs = useCallback(
    async (token: string) => {
      try {
        const queryParams = new URLSearchParams({
          query: filterQuery,
          artist: filterArtist,
          status: filterStatus,
          page: "1",
          pageSize: "50",
        }).toString();
        const response = await fetch(`${API_ROUTES.SONGS_MANAGE}?${queryParams}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const text = await response.text();
        if (!response.ok) {
          throw new Error(`Failed to fetch manageable songs: ${response.status} ${response.statusText} - ${text}`);
        }
        const data = JSON.parse(text);
        setManageableSongs(data.songs || []);
        setError(null);
      } catch (err) {
        const message = err instanceof Error ? err.message : "Unknown error";
        setError(message);
        setManageableSongs([]);
      }
    },
    [filterQuery, filterArtist, filterStatus]
  );

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    const storedRoles = localStorage.getItem("roles");
    if (storedRoles) {
      const parsedRoles = JSON.parse(storedRoles);
      if (!parsedRoles.includes("Song Manager")) {
        navigate("/dashboard");
        return;
      }
    } else {
      navigate("/login");
      return;
    }

    fetchManageableSongs(token);
  }, [navigate, fetchManageableSongs]);

  const handleEditSong = async (token: string) => {
    if (!editSong) return;
    try {
      const response = await fetch(`${API_ROUTES.SONG_UPDATE}/${editSong.Id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(editSong),
      });
      if (!response.ok) {
        throw new Error(`Failed to update song: ${response.status} ${response.statusText}`);
      }
      alert("Song updated successfully!");
      fetchManageableSongs(token);
      setEditSong(null);
      setError(null);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  };

  const handleDeleteSong = async (id: number, token: string) => {
    try {
      const response = await fetch(`${API_ROUTES.SONG_DELETE}/${id}`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        throw new Error(`Failed to delete song: ${response.status} ${response.statusText}`);
      }
      alert("Song deleted successfully!");
      fetchManageableSongs(token);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  };

  const handleClearYouTubeUrl = async (id: number, token: string) => {
    try {
      const song = manageableSongs.find(s => s.Id === id);
      if (!song) throw new Error("Song not found");
      const updatedSong = { ...song, YouTubeUrl: null };
      const response = await fetch(`${API_ROUTES.SONG_UPDATE}/${id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(updatedSong),
      });
      if (!response.ok) {
        throw new Error(`Failed to clear YouTube URL: ${response.status} ${response.statusText}`);
      }
      alert("YouTube URL cleared successfully!");
      fetchManageableSongs(token);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
    }
  };

  return (
    <div className="song-manager-container mobile-song-manager">
      <header className="song-manager-header">
        <h1 className="song-manager-title">Song Manager</h1>
        <div className="header-buttons">
          <button
            className="song-manager-button channels-button"
            onClick={() => navigate("/karaoke-channels")}
            onTouchStart={() => navigate("/karaoke-channels")}
          >
            Manage Channels
          </button>
          <button
            className="song-manager-button back-button"
            onClick={() => navigate("/dashboard")}
            onTouchStart={() => navigate("/dashboard")}
          >
            Back to Dashboard
          </button>
        </div>
      </header>

      <div className="song-manager-content">
        <section className="song-editor-card">
          <h2 className="section-title">Manage Songs</h2>
          <div className="filter-section">
            <input
              type="text"
              value={filterQuery}
              onChange={(e) => setFilterQuery(e.target.value)}
              placeholder="Search by title or artist"
              className="song-manager-input"
            />
            <input
              type="text"
              value={filterArtist}
              onChange={(e) => setFilterArtist(e.target.value)}
              placeholder="Filter by artist"
              className="song-manager-input"
            />
            <select
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value)}
              className="song-manager-input"
            >
              <option value="">All Statuses</option>
              <option value="active">Active</option>
              <option value="pending">Pending</option>
              <option value="unavailable">Unavailable</option>
            </select>
            <button
              className="song-manager-button filter-button"
              onClick={() => {
                const token = validateToken();
                if (token) fetchManageableSongs(token);
              }}
              onTouchStart={() => {
                const token = validateToken();
                if (token) fetchManageableSongs(token);
              }}
            >
              Apply Filters
            </button>
          </div>
          {error && <p className="error-text">{error}</p>}
          {manageableSongs.length > 0 ? (
            <ul className="song-list">
              {manageableSongs.map(song => (
                <li key={song.Id} className="song-item">
                  <div className="song-info">
                    <p className="song-title">{song.Title} - {song.Artist}</p>
                    <p className="song-text">Genre: {song.Genre || "Unknown"} | Status: {song.Status}</p>
                  </div>
                  <div className="song-actions">
                    <button
                      className="song-manager-button edit-button"
                      onClick={() => setEditSong(song)}
                      onTouchStart={() => setEditSong(song)}
                    >
                      Edit
                    </button>
                    <button
                      className="song-manager-button clear-url-button"
                      onClick={() => {
                        const token = validateToken();
                        if (token) handleClearYouTubeUrl(song.Id, token);
                      }}
                      onTouchStart={() => {
                        const token = validateToken();
                        if (token) handleClearYouTubeUrl(song.Id, token);
                      }}
                    >
                      Clear YouTube URL
                    </button>
                    <button
                      className="song-manager-button delete-button"
                      onClick={() => {
                        const token = validateToken();
                        if (token) handleDeleteSong(song.Id, token);
                      }}
                      onTouchStart={() => {
                        const token = validateToken();
                        if (token) handleDeleteSong(song.Id, token);
                      }}
                    >
                      Delete
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          ) : (
            <p className="song-manager-text">No songs match the filters.</p>
          )}
        </section>
      </div>

      {editSong && (
        <div className="modal-overlay mobile-song-manager">
          <div className="modal-content">
            <h2 className="modal-title">Edit Song</h2>
            <div className="edit-form">
              <input
                type="text"
                value={editSong.Title || ""}
                onChange={(e) => setEditSong({ ...editSong, Title: e.target.value })}
                placeholder="Title"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.Artist || ""}
                onChange={(e) => setEditSong({ ...editSong, Artist: e.target.value })}
                placeholder="Artist"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.Genre || ""}
                onChange={(e) => setEditSong({ ...editSong, Genre: e.target.value })}
                placeholder="Genre"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.Decade || ""}
                onChange={(e) => setEditSong({ ...editSong, Decade: e.target.value })}
                placeholder="Decade (e.g., 1980s)"
                className="song-manager-input"
              />
              <input
                type="number"
                step="0.1"
                value={editSong.Bpm || ""}
                onChange={(e) => setEditSong({ ...editSong, Bpm: parseFloat(e.target.value) || null })}
                placeholder="BPM"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.Danceability || ""}
                onChange={(e) => setEditSong({ ...editSong, Danceability: e.target.value })}
                placeholder="Danceability (e.g., danceable)"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.Energy || ""}
                onChange={(e) => setEditSong({ ...editSong, Energy: e.target.value })}
                placeholder="Energy (e.g., aggressive)"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.Mood || ""}
                onChange={(e) => setEditSong({ ...editSong, Mood: e.target.value })}
                placeholder="Mood (e.g., happy)"
                className="song-manager-input"
              />
              <input
                type="number"
                value={editSong.Popularity || ""}
                onChange={(e) => setEditSong({ ...editSong, Popularity: parseInt(e.target.value) || null })}
                placeholder="Popularity"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.SpotifyId || ""}
                onChange={(e) => setEditSong({ ...editSong, SpotifyId: e.target.value })}
                placeholder="Spotify ID"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.YouTubeUrl || ""}
                onChange={(e) => setEditSong({ ...editSong, YouTubeUrl: e.target.value })}
                placeholder="YouTube URL"
                className="song-manager-input"
              />
              <input
                type="text"
                value={editSong.MusicBrainzId || ""}
                onChange={(e) => setEditSong({ ...editSong, MusicBrainzId: e.target.value })}
                placeholder="MusicBrainz ID"
                className="song-manager-input"
              />
              <input
                type="number"
                value={editSong.LastFmPlaycount || ""}
                onChange={(e) => setEditSong({ ...editSong, LastFmPlaycount: parseInt(e.target.value) || null })}
                placeholder="Last.fm Playcount"
                className="song-manager-input"
              />
              <input
                type="number"
                value={editSong.Valence || ""}
                onChange={(e) => setEditSong({ ...editSong, Valence: parseInt(e.target.value) || null })}
                placeholder="Valence"
                className="song-manager-input"
              />
              <select
                value={editSong.Status || ""}
                onChange={(e) => setEditSong({ ...editSong, Status: e.target.value })}
                className="song-manager-input"
              >
                <option value="active">Active</option>
                <option value="pending">Pending</option>
                <option value="unavailable">Unavailable</option>
              </select>
            </div>
            <div className="modal-buttons">
              <button
                className="song-manager-button save-button"
                onClick={() => {
                  const token = validateToken();
                  if (token) handleEditSong(token);
                }}
                onTouchStart={() => {
                  const token = validateToken();
                  if (token) handleEditSong(token);
                }}
              >
                Save
              </button>
              <button
                className="song-manager-button close-button"
                onClick={() => setEditSong(null)}
                onTouchStart={() => setEditSong(null)}
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default SongManagerPage;

