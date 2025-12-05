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

interface SongApi {
  Id?: number;
  id?: number;
  Title?: string;
  title?: string;
  Artist?: string;
  artist?: string;
  Genre?: string | null;
  genre?: string | null;
  Decade?: string | null;
  decade?: string | null;
  Bpm?: number | null;
  bpm?: number | null;
  Danceability?: string | null;
  danceability?: string | null;
  Energy?: string | null;
  energy?: string | null;
  Mood?: string | null;
  mood?: string | null;
  Popularity?: number | null;
  popularity?: number | null;
  SpotifyId?: string | null;
  spotifyId?: string | null;
  YouTubeUrl?: string | null;
  youTubeUrl?: string | null;
  youtubeUrl?: string | null;
  Status?: string;
  status?: string;
  MusicBrainzId?: string | null;
  musicBrainzId?: string | null;
  LastFmPlaycount?: number | null;
  lastFmPlaycount?: number | null;
  Valence?: number | null;
  valence?: number | null;
}

interface SearchResult {
  Id: number;
  Title: string;
  Artist: string;
}

const SongManagerPage: React.FC = () => {
  const navigate = useNavigate();
  const [manageableSongs, setManageableSongs] = useState<SongUpdate[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [editSong, setEditSong] = useState<SongUpdate | null>(null);
  const [filterQuery, setFilterQuery] = useState("");
  const [filterArtist, setFilterArtist] = useState("");
  const [filterStatus, setFilterStatus] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState<SearchResult[]>([]);
  const [searching, setSearching] = useState(false);

  const validateToken = useCallback(() => {
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
  }, [navigate]);

  const mapSongApiToUpdate = useCallback((song: SongApi): SongUpdate => {
    return {
      Id: song.Id ?? song.id ?? 0,
      Title: song.Title ?? song.title ?? "",
      Artist: song.Artist ?? song.artist ?? "",
      Genre: song.Genre ?? song.genre ?? null,
      Decade: song.Decade ?? song.decade ?? null,
      Bpm: song.Bpm ?? song.bpm ?? null,
      Danceability: song.Danceability ?? song.danceability ?? null,
      Energy: song.Energy ?? song.energy ?? null,
      Mood: song.Mood ?? song.mood ?? null,
      Popularity: song.Popularity ?? song.popularity ?? null,
      SpotifyId: song.SpotifyId ?? song.spotifyId ?? null,
      YouTubeUrl: song.YouTubeUrl ?? song.youTubeUrl ?? song.youtubeUrl ?? null,
      Status: song.Status ?? song.status ?? "pending",
      MusicBrainzId: song.MusicBrainzId ?? song.musicBrainzId ?? null,
      LastFmPlaycount: song.LastFmPlaycount ?? song.lastFmPlaycount ?? null,
      Valence: song.Valence ?? song.valence ?? null,
    };
  }, []);

  const fetchManageableSongs = useCallback(
    async (token: string) => {
      try {
        const queryParams = new URLSearchParams({
          query: filterQuery,
          artist: filterArtist,
          status: filterStatus,
          page: "1",
          pageSize: "75",
        }).toString();
        const response = await fetch(`${API_ROUTES.SONGS_MANAGE}?${queryParams}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        const text = await response.text();
        if (!response.ok) {
          throw new Error(`Failed to fetch manageable songs: ${response.status} ${response.statusText} - ${text}`);
        }
        const data = JSON.parse(text);
        const normalized = (data.songs || []).map(mapSongApiToUpdate);
        setManageableSongs(normalized);
        setError(null);
      } catch (err) {
        const message = err instanceof Error ? err.message : "Unknown error";
        setError(message);
        setManageableSongs([]);
      }
    },
    [filterQuery, filterArtist, filterStatus, mapSongApiToUpdate]
  );

  const handleSearch = useCallback(async () => {
    const trimmedQuery = searchQuery.trim();
    if (!trimmedQuery) {
      setSearchResults([]);
      return;
    }
    const token = validateToken();
    if (!token) return;
    setSearching(true);
    setError(null);
    try {
      const response = await fetch(`${API_ROUTES.SONGS_SEARCH}?query=${encodeURIComponent(trimmedQuery)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (response.status === 401) {
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        navigate("/login");
        return;
      }
      const text = await response.text();
      if (!response.ok) {
        throw new Error(`Search failed: ${response.status} ${response.statusText} - ${text}`);
      }
      const data = JSON.parse(text);
      const results = (data.songs || [])
        .map((song: SongApi) => ({
          Id: song.Id ?? song.id ?? 0,
          Title: song.Title ?? song.title ?? "Untitled",
          Artist: song.Artist ?? song.artist ?? "Unknown Artist",
        }))
        .filter((result) => result.Id > 0);
      setSearchResults(results);
      if (results.length === 0) {
        setError(`No songs matched "${trimmedQuery}".`);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : "Unknown error";
      setError(message);
      setSearchResults([]);
    } finally {
      setSearching(false);
    }
  }, [navigate, searchQuery, validateToken]);

  const handleSelectSearchResult = useCallback(
    async (songId: number) => {
      const token = validateToken();
      if (!token) return;
      try {
        const response = await fetch(`${API_ROUTES.SONG_BY_ID}/${songId}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (response.status === 401) {
          localStorage.removeItem("token");
          localStorage.removeItem("userName");
          navigate("/login");
          return;
        }
        if (!response.ok) {
          throw new Error("Failed to load song details");
        }
        const songData: SongApi = await response.json();
        setEditSong(mapSongApiToUpdate(songData));
        setSearchResults([]);
      } catch (err) {
        const message = err instanceof Error ? err.message : "Unknown error";
        setError(message);
      }
    },
    [mapSongApiToUpdate, navigate, validateToken]
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
  }, [navigate, fetchManageableSongs, validateToken]);

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

  const handleResetSong = async (id: number, token: string) => {
    try {
      const response = await fetch(`${API_ROUTES.SONG_BY_ID}/${id}/reset-video`, {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        throw new Error(`Failed to reset song: ${response.status} ${response.statusText}`);
      }
      alert("Song reset to pending successfully!");
      await fetchManageableSongs(token);
      setSearchResults([]);
      setError(null);
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

          >
            Manage Channels
          </button>
          <button
            className="song-manager-button back-button"
            onClick={() => navigate("/dashboard")}

          >
            Back to Dashboard
          </button>
        </div>
      </header>

      <div className="song-manager-content">
        <section className="song-manager-card song-search-section">
          <div className="song-manager-section-heading">
            <h2 className="section-title">Search &amp; Edit Videos</h2>
            <p className="section-subtitle">
              Look up any song by title or artist and open it in the editor.
            </p>
          </div>
          <div className="search-controls">
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  handleSearch();
                }
              }}
              placeholder="Enter title or artist"
              className="song-manager-input"
            />
            <button
              className="song-manager-button edit-button"
              onClick={handleSearch}
              disabled={searching || searchQuery.trim() === ""}
            >
              {searching ? "Searching…" : "Search"}
            </button>
          </div>
          {searchResults.length > 0 ? (
            <ul className="search-results">
              {searchResults.map((result) => (
                <li key={`search-${result.Id}`} className="song-item search-result-card">
                  <article className="song-card">
                    <header className="song-card-header">
                      <div>
                        <h3 className="song-card-title">{result.Title}</h3>
                        <p className="song-card-subtitle">{result.Artist}</p>
                      </div>
                    </header>
                    <div className="song-actions song-card-actions">
                      <button
                        className="song-manager-button edit-button"
                        onClick={() => handleSelectSearchResult(result.Id)}
                      >
                        Edit
                      </button>
                      <button
                        className="song-manager-button reset-button"
                        onClick={() => {
                          const token = validateToken();
                          if (token) handleResetSong(result.Id, token);
                        }}
                      >
                        Reset to Pending
                      </button>
                    </div>
                  </article>
                </li>
              ))}
            </ul>
          ) : searchQuery.trim() ? (
            <p className="song-manager-text">No search results for "{searchQuery}".</p>
          ) : (
            <p className="song-manager-text">
              Use the search above to find a video you want to edit.
            </p>
          )}
        </section>
        <section className="song-editor-card song-manager-card">
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
              {manageableSongs.map((song, index) => (
                <li key={`${song.Id}-${index}`} className="song-item">
                  <article className="song-card">
                    <header className="song-card-header">
                      <div>
                        <h3 className="song-card-title">{song.Title || "Untitled"}</h3>
                        <p className="song-card-subtitle">{song.Artist || "Unknown artist"}</p>
                      </div>
                      <span className={`song-card-status status-${(song.Status || "unknown").toLowerCase()}`}>
                        {song.Status}
                      </span>
                    </header>
                    <div className="song-card-meta">
                      <p className="song-card-meta-line">
                        Genre: {song.Genre || "Unknown"} &middot; Decade: {song.Decade || "Unknown"}
                      </p>
                      <p className="song-card-meta-line">
                        YouTube: {song.YouTubeUrl ? <span className="song-link">{song.YouTubeUrl}</span> : "N/A"}
                      </p>
                    </div>
                    <div className="song-actions song-card-actions">
                      <button
                        className="song-manager-button edit-button"
                        onClick={() => setEditSong(song)}
                      >
                        Edit
                      </button>
                      <button
                        className="song-manager-button clear-url-button"
                        onClick={() => {
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
                      >
                        Delete
                      </button>
                      <button
                        className="song-manager-button reset-button"
                        onClick={() => {
                          const token = validateToken();
                          if (token) handleResetSong(song.Id, token);
                        }}
                      >
                        Reset to Pending
                      </button>
                    </div>
                  </article>
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

              >
                Save
              </button>
              <button
                className="song-manager-button close-button"
                onClick={() => setEditSong(null)}

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

