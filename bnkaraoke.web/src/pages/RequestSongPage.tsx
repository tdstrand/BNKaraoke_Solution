// src/pages/RequestSongPage.tsx
import React, { useState, useEffect, useCallback } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "../pages/Dashboard.css";
import { SpotifySong } from "../types";

const RequestSongPage: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const initialQuery = location.state && location.state.searchQuery ? location.state.searchQuery : "";
  const [searchQuery, setSearchQuery] = useState<string>(initialQuery);
  const [searchResults, setSearchResults] = useState<SpotifySong[]>([]);
  const [error, setError] = useState<string | null>(null);

  const validateToken = () => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[REQUEST_SONG] No token or userName found");
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[REQUEST_SONG] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[REQUEST_SONG] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[REQUEST_SONG] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[REQUEST_SONG] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  };

  const handleSearch = useCallback(async () => {
    const token = validateToken();
    if (!token) return;

    try {
      console.log(`[REQUEST_SONG] Fetching Spotify songs with query: ${searchQuery}`);
      const response = await fetch(`${API_ROUTES.SPOTIFY_SEARCH}?query=${encodeURIComponent(searchQuery)}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[REQUEST_SONG] Request Song Search Raw Response:", responseText);
      if (!response.ok) throw new Error(`Failed to search: ${response.status} ${response.statusText} - ${responseText}`);
      const data = JSON.parse(responseText);
      setSearchResults(data.songs || []);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      setSearchResults([]);
      console.error("[REQUEST_SONG] Search error:", err);
    }
  }, [searchQuery]);

  useEffect(() => {
    if (searchQuery) {
      handleSearch();
    }
  }, [searchQuery, handleSearch]);

  const handleRequestSong = async (song: SpotifySong) => {
    const token = validateToken();
    if (!token) return;

    const requestBody = {
      title: song.title,
      artist: song.artist,
      spotifyId: song.id,
      bpm: song.bpm || 0,
      danceability: song.danceability || 0,
      energy: song.energy || 0,
      popularity: song.popularity || 0,
      genre: song.genre || null,
      status: "pending",
      requestDate: new Date().toISOString(),
      requestedBy: localStorage.getItem("userName") || "12345678901",
    };

    try {
      console.log(`[REQUEST_SONG] Requesting song: ${song.title} by ${song.artist}`);
      const response = await fetch(API_ROUTES.REQUEST_SONG, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(requestBody),
      });
      const responseText = await response.text();
      console.log("[REQUEST_SONG] Request Song Raw Response:", responseText);
      if (!response.ok) throw new Error(`Failed to request song: ${response.status} ${response.statusText} - ${responseText}`);
      alert("Song requested successfully!");
      navigate("/dashboard");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      console.error("[REQUEST_SONG] Request song error:", err);
    }
  };

  try {
    return (
      <div className="dashboard-container">
        <header className="dashboard-header">
          <h1 className="dashboard-title">Request a Karaoke Song</h1>
          <div className="header-buttons">
            <button className="dashboard-button back-button" onClick={() => navigate("/dashboard")}>
              Back to Dashboard
            </button>
          </div>
        </header>
        <div className="dashboard-card">
          <div className="search-bar">
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search for a song..."
              className="form-input"
            />
            <button onClick={handleSearch} className="dashboard-button">
              Search
            </button>
          </div>
          {error && <p className="error-text">{error}</p>}
          {searchResults.length > 0 ? (
            <ul className="song-list">
              {searchResults.map((song) => (
                <li key={song.id} className="song-item">
                  <div>
                    <p className="song-title">{song.title} - {song.artist}</p>
                    <p className="song-text">Genre: {song.genre || "Unknown"}</p>
                  </div>
                  <button
                    className="dashboard-button action-button"
                    onClick={() => handleRequestSong(song)}
                  >
                    Request This Song
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p className="dashboard-text">No results yet. Search for a song!</p>
          )}
        </div>
      </div>
    );
  } catch (error) {
    console.error("[REQUEST_SONG] Render error:", error);
    return <div>Error in RequestSongPage: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default RequestSongPage;